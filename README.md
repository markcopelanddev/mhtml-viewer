# MHTML Viewer

MHTML Viewer is a small Windows app for reading saved web pages like a book.

If you have a folder full of `.mhtml` or `.mht` captures from a website, course, manual, wiki, or online book, normal browser workflows get clumsy fast. You can open one page, but moving through hundreds of saved pages in order feels nothing like reading the original site.

MHTML Viewer turns that folder into a simple reader: choose the folder, start at any file, then flip backward and forward through the saved pages.

## Why This Exists

Saved web pages are useful, but reading them later is usually awkward:

- File Explorer sorts numbered pages badly unless names are padded.
- Browsers open each page as a separate one-off file.
- Jumping to the middle of a saved book or page collection takes too many clicks.
- Browser zoom jumps in coarse steps when you just need a small adjustment.

This app is built for the boring but real use case: you saved a lot of pages and now you want to read them comfortably.

## What It Does

- Opens folders containing `.mhtml`, `.mht`, and `.pdf` files.
- Sorts files naturally, so `2` comes before `10`.
- Lets you select a specific file and start reading from that point.
- Moves backward and forward like page navigation in a book.
- Adds a bottom slider so you can jump through the full folder quickly.
- Supports keyboard navigation with arrows, PageUp/PageDown, and Space.
- Provides precise zoom controls with 1% and 5% increments.
- Uses Microsoft WebView2 so pages render like real browser content.

## Current Version

`1.1.0` is the main version in:

```text
src/MHTML Viewer
```

New in `1.1.0`:

- `Select File` button for jumping directly to a page in its folder.
- Bottom slider for fast navigation through every supported file.

## Legacy Version

`legacy/1.0.0` contains the original version before the file-jump and slider update.

## What This App Is Not

MHTML Viewer does not download websites, scrape pages, sync content, or upload anything. It only reads local files that you select on your own computer.

## Requirements

- Windows 11
- Microsoft Edge WebView2 Runtime, normally included with Windows 11
- .NET 8 SDK if building from source

## Build

From the repository root:

```powershell
dotnet build "src/MHTML Viewer/MHTML Viewer.csproj"
```

## Run From Source

```powershell
dotnet run --project "src/MHTML Viewer/MHTML Viewer.csproj"
```

## Publish A Local Windows Build

```powershell
dotnet publish "src/MHTML Viewer/MHTML Viewer.csproj" -c Release -o "release/MHTML Viewer"
```

Then run:

```powershell
.\release\MHTML Viewer\MHTML Viewer.exe
```

## Privacy

The app works locally. It does not collect analytics, make network calls for your files, or send page contents anywhere.

