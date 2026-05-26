# AIO Hybrid Clipboard 

<img width="993" height="157" alt="Ekran görüntüsü 2026-05-26 130655" src="https://github.com/user-attachments/assets/9c12bc4a-bfe8-42e9-a5ee-b26ecd808332" />


![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?style=flat-square&logo=windows)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)
![C++](https://img.shields.io/badge/Engine-C%2B%2B%2020-00599C?style=flat-square&logo=c%2B%2B)
![C#](https://img.shields.io/badge/Frontend-C%23%20WPF-239120?style=flat-square&logo=c-sharp)

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
* * **Quick-paste hotkeys:** `ALT + 1, ALT + 2, ALT + 3`

## Tech Stack
* **Frontend:** C# / .NET / WPF
* **Backend OCR Engine:** C++20 / WinRT API (`Windows.Media.Ocr`)
* **Communication:** P/Invoke (Unmanaged to Managed)

## How to Use
1. Press `ALT + SPACE` to open the interface.
2. Copy any text or capture any image. They will automatically populate the lists.
3. **Single-click** an image to extract its text via OCR.
4. **Click and drag** an image to export the PNG file.
