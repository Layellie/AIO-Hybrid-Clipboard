#include "pch.h"

#include <roapi.h>
#include <MemoryBuffer.h>
#include <cstdint>
#include <cstring>
#include <string>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Globalization.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.Media.Ocr.h>

#pragma comment(lib, "windowsapp.lib")

using namespace winrt;
using namespace winrt::Windows::Globalization;
using namespace winrt::Windows::Graphics::Imaging;
using namespace winrt::Windows::Media::Ocr;

namespace
{
    // Status codes returned to managed callers.
    // Keep in sync with OcrService.OcrStatus in the C# project.
    constexpr int kOcrSuccess         = 0;
    constexpr int kOcrNoText          = 1;
    constexpr int kOcrEngineFailed    = -1;
    constexpr int kOcrException       = -2;
    constexpr int kOcrInvalidArgument = -3;

    // Upper dimension guard to prevent integer overflow and runaway allocations.
    constexpr int kMaxImageDimension = 16384;

    // RAII apartment guard: initializes MTA when uninitialized and balances
    // RoUninitialize on exit. Tolerates threads already initialized as STA/MTA.
    class WinRtApartmentGuard final
    {
    public:
        WinRtApartmentGuard() noexcept
        {
            const HRESULT hr = ::RoInitialize(RO_INIT_MULTITHREADED);
            if (SUCCEEDED(hr))
            {
                _shouldUninitialize = true;
                _ok = true;
            }
            else if (hr == RPC_E_CHANGED_MODE)
            {
                // Thread already initialized with another apartment model; WinRT
                // calls still work without calling RoUninitialize.
                _shouldUninitialize = false;
                _ok = true;
            }
        }

        ~WinRtApartmentGuard() noexcept
        {
            if (_shouldUninitialize)
                ::RoUninitialize();
        }

        WinRtApartmentGuard(const WinRtApartmentGuard&) = delete;
        WinRtApartmentGuard& operator=(const WinRtApartmentGuard&) = delete;

        bool IsReady() const noexcept { return _ok; }

    private:
        bool _shouldUninitialize = false;
        bool _ok = false;
    };

    OcrEngine CreateOcrEngine(const wchar_t* languageTag = nullptr)
    {
        if (languageTag && languageTag[0] != L'\0' && _wcsicmp(languageTag, L"auto") != 0)
        {
            try
            {
                Language lang(languageTag);
                if (OcrEngine::IsLanguageSupported(lang))
                {
                    OcrEngine specific = OcrEngine::TryCreateFromLanguage(lang);
                    if (specific)
                        return specific;
                }
            }
            catch (...)
            {
                // Invalid or unavailable tag — fall back to user profile languages below.
            }
        }

        OcrEngine engine = OcrEngine::TryCreateFromUserProfileLanguages();
        if (!engine)
            engine = OcrEngine::TryCreateFromLanguage(Language(L"en-US"));
        return engine;
    }

    // Preserves visual line breaks from OcrResult::Lines() instead of flattening
    // everything into a single space-separated paragraph via OcrResult::Text().
    std::wstring ExtractStructuredText(const OcrResult& result)
    {
        const auto lines = result.Lines();
        if (lines.Size() == 0)
            return std::wstring{ result.Text() };

        std::wstring output;
        output.reserve(256);

        bool first = true;
        for (const auto& line : lines)
        {
            const std::wstring lineText{ line.Text() };
            if (lineText.empty())
                continue;

            if (!first)
                output.append(L"\r\n");
            output.append(lineText);
            first = false;
        }
        return output;
    }
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID)
{
    return TRUE;
}

// Returns 1 if a Windows OCR engine can be instantiated on this machine, 0 otherwise.
extern "C" __declspec(dllexport) int __cdecl IsOcrEngineAvailable()
{
    try
    {
        WinRtApartmentGuard apartment;
        if (!apartment.IsReady())
            return 0;

        return CreateOcrEngine() ? 1 : 0;
    }
    catch (...)
    {
        return 0;
    }
}

// Writes installed Windows OCR language tags separated by ';' (e.g. "en-US;tr-TR") to outText.
// Returns the number of languages found, or 0 on failure.
extern "C" __declspec(dllexport) int __cdecl GetAvailableOcrLanguages(
    wchar_t* outText,
    int maxLen
)
{
    if (!outText || maxLen <= 0)
        return 0;

    outText[0] = L'\0';

    try
    {
        WinRtApartmentGuard apartment;
        if (!apartment.IsReady())
            return 0;

        const auto languages = OcrEngine::AvailableRecognizerLanguages();
        std::wstring joined;
        int count = 0;

        for (const auto& lang : languages)
        {
            const std::wstring tag{ lang.LanguageTag() };
            if (tag.empty())
                continue;

            if (count > 0)
                joined.push_back(L';');
            joined.append(tag);
            ++count;
        }

        if (count > 0)
            wcsncpy_s(outText, static_cast<size_t>(maxLen), joined.c_str(), _TRUNCATE);

        return count;
    }
    catch (...)
    {
        return 0;
    }
}

// Runs the Windows OCR engine over raw premultiplied BGRA8 pixel data for a specific language tag
// (pass nullptr or L"auto" to use Windows user profile languages).
extern "C" __declspec(dllexport) int __cdecl ProcessImageOCRForLanguage(
    const unsigned char* pixelData,
    int width,
    int height,
    int stride,
    const wchar_t* languageTag,
    wchar_t* outText,
    int maxLen
)
{
    if (!pixelData || !outText || maxLen <= 0 ||
        width <= 0 || height <= 0 ||
        width > kMaxImageDimension || height > kMaxImageDimension)
    {
        return kOcrInvalidArgument;
    }

    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    if (stride < 0 || static_cast<size_t>(stride) < rowBytes)
        return kOcrInvalidArgument;

    outText[0] = L'\0';

    try
    {
        WinRtApartmentGuard apartment;
        if (!apartment.IsReady())
            return kOcrEngineFailed;

        OcrEngine engine = CreateOcrEngine(languageTag);
        if (!engine)
            return kOcrEngineFailed;

        // Copy pixel rows directly into the SoftwareBitmap backing buffer (single copy).
        SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, width, height, BitmapAlphaMode::Premultiplied);
        {
            BitmapBuffer buffer = bitmap.LockBuffer(BitmapBufferAccessMode::Write);
            auto reference = buffer.CreateReference();
            auto byteAccess = reference.as<::Windows::Foundation::IMemoryBufferByteAccess>();

            uint8_t* dest = nullptr;
            uint32_t capacity = 0;
            winrt::check_hresult(byteAccess->GetBuffer(&dest, &capacity));

            const BitmapPlaneDescription plane = buffer.GetPlaneDescription(0);
            if (plane.StartIndex < 0 || plane.Stride < 0 ||
                static_cast<size_t>(plane.Stride) < rowBytes)
            {
                return kOcrException;
            }

            const size_t dstStride = static_cast<size_t>(plane.Stride);
            const size_t srcStride = static_cast<size_t>(stride);
            const size_t requiredCapacity =
                static_cast<size_t>(plane.StartIndex) +
                static_cast<size_t>(height - 1) * dstStride +
                rowBytes;

            if (!dest || requiredCapacity > static_cast<size_t>(capacity))
                return kOcrException;

            uint8_t* dstBase = dest + static_cast<size_t>(plane.StartIndex);
            if (dstStride == srcStride && dstStride == rowBytes)
            {
                std::memcpy(dstBase, pixelData, rowBytes * static_cast<size_t>(height));
            }
            else
            {
                for (int y = 0; y < height; ++y)
                {
                    std::memcpy(
                        dstBase + static_cast<size_t>(y) * dstStride,
                        pixelData + static_cast<size_t>(y) * srcStride,
                        rowBytes);
                }
            }
        }

        OcrResult result = engine.RecognizeAsync(bitmap).get();
        const std::wstring recognizedText = ExtractStructuredText(result);
        if (recognizedText.empty())
            return kOcrNoText;

        wcsncpy_s(outText, static_cast<size_t>(maxLen), recognizedText.c_str(), _TRUNCATE);
        return kOcrSuccess;
    }
    catch (...)
    {
        // Never allow native exceptions to cross the P/Invoke boundary.
        return kOcrException;
    }
}

// Backward-compatible default entry point using Windows user profile OCR languages.
extern "C" __declspec(dllexport) int __cdecl ProcessImageOCR(
    const unsigned char* pixelData,
    int width,
    int height,
    int stride,
    wchar_t* outText,
    int maxLen
)
{
    return ProcessImageOCRForLanguage(pixelData, width, height, stride, nullptr, outText, maxLen);
}
