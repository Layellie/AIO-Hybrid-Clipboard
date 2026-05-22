#include "pch.h"
#include <windows.h>
#include <string>
#include <vector>

// Windows Runtime C++/WinRT headers for OCR and Imaging API
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.Media.Ocr.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Globalization.h>

// Link the required Windows Runtime core library
#pragma comment(lib, "windowsapp.lib")

using namespace winrt;
using namespace winrt::Windows::Graphics::Imaging;
using namespace winrt::Windows::Media::Ocr;
using namespace winrt::Windows::Storage::Streams;
using namespace winrt::Windows::Globalization;

// DLL Entry Point
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    return TRUE;
}

// Main OCR Processing Function exported for C# P/Invoke
extern "C" __declspec(dllexport) void __cdecl ProcessImageOCR(
    const unsigned char* pixelData,
    int width,
    int height,
    int stride,
    wchar_t* outText,
    int maxLen
)
{
    try {
        // Initialize the WinRT apartment state for multi-threaded safety
        winrt::init_apartment(winrt::apartment_type::multi_threaded);

        // Write raw pixel data into a WinRT data buffer
        DataWriter writer;
        writer.WriteBytes(winrt::array_view<const uint8_t>(pixelData, pixelData + (height * stride)));
        IBuffer buffer = writer.DetachBuffer();

        // Create a SoftwareBitmap from the buffer using BGRA8 format matching WPF specifications
        SoftwareBitmap bitmap = SoftwareBitmap::CreateCopyFromBuffer(buffer, BitmapPixelFormat::Bgra8, width, height, BitmapAlphaMode::Premultiplied);

        // Initialize OCR engine using the system's configured user languages (supports EN and TR simultaneously)
        OcrEngine engine = OcrEngine::TryCreateFromUserProfileLanguages();
        if (!engine) {
            // Fallback to standard English if user profile languages are unavailable
            engine = OcrEngine::TryCreateFromLanguage(Language(L"en-US"));
        }

        if (engine) {
            // Execute synchronous OCR recognition process
            OcrResult result = engine.RecognizeAsync(bitmap).get();
            std::wstring recognizedText = result.Text().c_str();

            if (recognizedText.empty()) {
                wcscpy_s(outText, maxLen, L"[OCR: No readable text found in the image]");
            }
            else {
                wcscpy_s(outText, maxLen, recognizedText.c_str());
            }
        }
        else {
            wcscpy_s(outText, maxLen, L"[OCR Error: Windows OCR engine failed to initialize]");
        }
    }
    catch (...) {
        // Prevent application crashes by catching unexpected memory or structural exceptions
        wcscpy_s(outText, maxLen, L"[OCR Error: A memory exception occurred during processing]");
    }
}