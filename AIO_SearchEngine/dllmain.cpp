#include "pch.h"
#include <windows.h>
#include <cstring>
#include <string>

// Windows Runtime C++/WinRT headers for OCR and Imaging API
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.Media.Ocr.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Globalization.h>
#include <MemoryBuffer.h>

// Link the required Windows Runtime core library
#pragma comment(lib, "windowsapp.lib")

using namespace winrt;
using namespace winrt::Windows::Graphics::Imaging;
using namespace winrt::Windows::Media::Ocr;
using namespace winrt::Windows::Globalization;

namespace
{
    // Status codes returned to the managed caller.
    // Keep in sync with OcrService.OcrStatus in the C# project.
    constexpr int kOcrSuccess         = 0;
    constexpr int kOcrNoText          = 1;
    constexpr int kOcrEngineFailed    = -1;
    constexpr int kOcrException       = -2;
    constexpr int kOcrInvalidArgument = -3;
}

// DLL Entry Point
BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID)
{
    return TRUE;
}

// Runs the Windows OCR engine over raw BGRA8 pixel data.
// Returns a status code; recognized text is written to outText only on success.
extern "C" __declspec(dllexport) int __cdecl ProcessImageOCR(
    const unsigned char* pixelData,
    int width,
    int height,
    int stride,
    wchar_t* outText,
    int maxLen
)
{
    if (!pixelData || !outText || maxLen <= 0 || width <= 0 || height <= 0 || stride < width * 4)
        return kOcrInvalidArgument;

    outText[0] = L'\0';

    try {
        // Initialize the WinRT apartment state for multi-threaded safety
        winrt::init_apartment(winrt::apartment_type::multi_threaded);

        // Copy the pixel rows straight into the SoftwareBitmap's backing buffer —
        // a single copy, instead of staging through an intermediate IBuffer.
        SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, width, height, BitmapAlphaMode::Premultiplied);
        {
            BitmapBuffer buffer = bitmap.LockBuffer(BitmapBufferAccessMode::Write);
            auto reference = buffer.CreateReference();
            auto byteAccess = reference.as<::Windows::Foundation::IMemoryBufferByteAccess>();

            uint8_t* dest = nullptr;
            uint32_t capacity = 0;
            winrt::check_hresult(byteAccess->GetBuffer(&dest, &capacity));

            const BitmapPlaneDescription plane = buffer.GetPlaneDescription(0);
            const int rowBytes = width * 4;
            for (int y = 0; y < height; ++y)
                memcpy(dest + plane.StartIndex + static_cast<size_t>(y) * plane.Stride,
                       pixelData + static_cast<size_t>(y) * stride,
                       rowBytes);
        }

        // Prefer the user's configured languages (supports EN and TR simultaneously),
        // falling back to standard English.
        OcrEngine engine = OcrEngine::TryCreateFromUserProfileLanguages();
        if (!engine)
            engine = OcrEngine::TryCreateFromLanguage(Language(L"en-US"));
        if (!engine)
            return kOcrEngineFailed;

        OcrResult result = engine.RecognizeAsync(bitmap).get();
        const std::wstring recognizedText{ result.Text() };
        if (recognizedText.empty())
            return kOcrNoText;

        wcsncpy_s(outText, maxLen, recognizedText.c_str(), _TRUNCATE);
        return kOcrSuccess;
    }
    catch (...) {
        // Never let a native exception cross the P/Invoke boundary.
        return kOcrException;
    }
}
