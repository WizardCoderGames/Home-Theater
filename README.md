# Eric's Home Theater

**Your movie collection, TV-style.** Eric's Home Theater turns a Windows PC into a living-room movie player: a full-screen, couch-friendly menu of your movies and shows that you control with a keyboard, a mouse, a remote, or your phone.

It plays the movie files already on your PC (MKV, MP4, AVI and more, up to 4K HDR) and DVDs, using the free VLC media player as its playback engine. There's no account, no subscription and no cloud: everything stays on your PC.

---

## What it does

- **Big-screen menu.** A home screen like a streaming app, with a large backdrop for the selected title and rows for Continue Watching, Recently Added, 4K & HDR, Collections & Series, and every genre.
- **Organizes your library.** It finds your movies in the folders you choose, cleans up messy file names (`Creepshow.1982.1080p.BluRay.x264-[YTS]` becomes **Creepshow (1982)**), groups series and collections, and grabs a scene from each movie as its thumbnail.
- **Genres.** Horror, Comedy, Action, Superhero, Sci-Fi, Thriller, Fantasy, Family & Animation, TV Series, Crime and Drama, plus Slashers, Zombies, Creature Features and Horror Comedy. Change any title's genres from its menu.
- **Remembers where you stopped.** Offers to resume, marks finished movies as watched, and plays the next episode of a series automatically.
- **Your phone is the remote.** Scan the QR code in Settings, type the 4-digit code that pops up on the TV, and you get a D-pad, play/pause, volume, audio and subtitle buttons, search, and your whole library to tap and play. Works in any phone's web browser; no app to install.
- **Search** by title, year or genre from the keyboard or your phone.
- **Discs.** Plug in a DVD or Blu-ray drive and a Disc row appears. DVDs and unprotected discs play directly (see *Discs* below).
- **USB drives.** Plug in a flash drive, SD card or external USB hard drive and its movies are added automatically, in a "USB" row on the home screen and in search, genres and All Movies. Unplug it and they disappear again. Turn this off in **Settings → USB drives**.
- **Runs well on slow PCs.** Animations switch off automatically if a PC can't draw them smoothly, and you can lower the screen resolution while the app is open (Native, 1080p, 720p).
- **Playback options for tricky hardware.** Choose hardware acceleration (Automatic, Direct3D 11, DXVA2 or off) and the video renderer (Direct3D 11, Direct3D 9, OpenGL or Windows GDI).
- **System info.** A live view of the processor, graphics card, memory, CPU and GPU load, display and network.
- **Updates itself.** When a new version is published here, the app downloads it, checks it, and updates and reopens itself; it never interrupts a movie.

## Requirements

- Windows 10 or Windows 11
- [VLC media player](https://www.videolan.org/vlc/) (free); Setup tells you if it's missing
- Your movies on the PC, an external drive, or a network folder

## Install

1. Download **EricsHomeTheater-Setup.exe** from the [latest release](https://github.com/DarkUndeadSpawn/Erics-Home-Theater/releases/latest).
2. Run it and choose **Install**. No administrator password is needed; it installs for your Windows account only.
3. Open **Eric's Home Theater** from the desktop or Start menu.
4. On first launch, Windows asks whether to allow it on your network. Allow it (tick **Public** too if your home Wi-Fi is marked as a public network) so the phone remote works.

Windows SmartScreen may warn that the installer isn't recognized, because it isn't digitally signed. Choose **More info → Run anyway**.

## Getting started

1. Open **Settings → Add a movie folder** and pick where your movies are. The library fills in within a few seconds; thumbnails appear over the next few minutes.
2. Use the arrow keys and **Enter**, or connect your phone (below), and pick something to watch.

### Connect your phone

1. On the TV, open **Settings**. The *Phone remote* panel shows a QR code.
2. Point your phone's camera at it (the phone must be on the same Wi-Fi).
3. A 4-digit code pops up on the TV. Type it on the phone.

Tip: in Chrome on Android, use **⋮ → Add to Home screen** so the remote opens like an app. For security, the code changes every time the app starts.

## Controls

| Action | Keyboard | Phone remote |
|---|---|---|
| Move around | Arrow keys | D-pad (tap or swipe) |
| Select / play / pause | Enter | OK, ⏯ |
| Back | Esc or Backspace | Back |
| Home (top of the home screen) | Home | Home |
| Movie options: genres, watched | F2 or the Menu key | Menu |
| Search | Ctrl+F, or just start typing | Search box |
| Skip back / forward | ← / → (10 s / 30 s) | ⏪ / ⏩ |
| Volume / mute | + / − / M | Volume buttons |
| Audio track / subtitles | A / S | Audio / Subtitles |
| System info | F1 | Info |
| Window or full screen | F11 | |

With a mouse, hover to highlight, click to choose, right-click to go back, and use the scroll wheel for volume during a movie. Pressing **Back** on the home screen asks before exiting.

## Discs

- **DVDs** play with their menus.
- **Unprotected discs and copied Blu-ray folders** play normally.
- **Store-bought Blu-ray and 4K discs are encrypted** and VLC can't play them. If you have a licensed disc player program, choose it in **Settings → Disc player** and the app opens it when you play a disc. Licensed 4K disc playback is not possible on most current PCs.

## Updates

The app checks this page shortly after starting and every 6 hours. When there's a new version it downloads it in the background, checks that it matches the published checksum, and shows **Update ready** with a 20-second countdown. If you don't answer, it updates and reopens on its own, keeping your library, settings and history. Updates wait until you're not watching a movie.

You can check right away, or turn automatic updates off, in **Settings → System**. Running an older installer installs the newest version instead.

## Privacy and your network

- Everything stays on your PC. There's no account and nothing is uploaded.
- The only internet connection the app makes is to GitHub, to check for and download updates.
- The phone remote works only on your local network and only after the code shown on the TV is entered. The code changes each time the app starts.

## Uninstall

**Windows Settings → Apps → Installed apps → Eric's Home Theater → Uninstall.** You'll be asked whether to also delete your settings, watch history and thumbnails. Your movie files are never touched.

## Troubleshooting

- **The phone can't connect:** make sure the phone and PC are on the same Wi-Fi, and that Windows Firewall allows the app (see *Install*, step 4).
- **A movie shows a black or green picture, or stutters:** try another option in **Settings → Hardware acceleration** or **Video renderer**.
- **Menus feel slow:** set **Settings → Animations** to *Off*, or **Screen resolution** to 1080p.
- **No movies show up:** check **Settings → Movie folder**, then choose **Rescan library**.
- **A USB drive's movies don't appear:** make sure **Settings → USB drives** is on. Internal drives are never added automatically; add them as a movie folder instead.

---

*Eric's Home Theater uses [VLC media player](https://www.videolan.org/) for playback. It is a personal project and isn't affiliated with VideoLAN, Netflix or any streaming service.*
