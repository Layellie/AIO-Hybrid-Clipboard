# AIO Hybrid Clipboard 

<img width="997" height="169" alt="Ekran görüntüsü 2026-05-22 180200" src="https://github.com/user-attachments/assets/033e7b90-0050-41a5-bca2-b0b19765913f" />

A blazing fast, lightweight, and hybrid (C# WPF + C++20) clipboard manager. AIO Hybrid Clipboard not only tracks your copied texts and images but also features a built-in, asynchronous C++ WinRT OCR engine to instantly extract text from your screenshot captures.

## Features

* **Hybrid Architecture:** Beautiful UI built with C# WPF, heavy lifting and pixel processing handled by a custom C++20 DLL for zero UI lag.
* **Smart OCR Engine:** Instantly extracts text from captured images using the native Windows WinRT AI engine. Just click on a saved screenshot!
* **Reverse OCR Search:** Search for words inside your images. The background engine indexes image texts so you can find them instantly.
* **Drag & Drop Ready:** Seamlessly drag images from the gallery directly into Discord, Photoshop, or your Desktop.
* **System Tray Integration:** Runs silently in the background with minimal memory footprint.
* **Global Shortcuts:** Fully customizable hotkeys to summon the launcher from anywhere.

## Default Shortcuts
* **Summon Launcher:** `ALT + SPACE`
* **Capture Screen (Windows Default):** `WIN + SHIFT + S` 

## Tech Stack
* **Frontend:** C# / .NET / WPF
* **Backend OCR Engine:** C++20 / WinRT API (`Windows.Media.Ocr`)
* **Communication:** P/Invoke (Unmanaged to Managed)

## How to Use
1. Press `ALT + SPACE` to open the interface.
2. Copy any text or capture any image. They will automatically populate the lists.
3. **Single-click** an image to extract its text via OCR.
4. **Click and drag** an image to export the PNG file.
