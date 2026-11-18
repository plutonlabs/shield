# Pluton Shield

**Block ads and trackers on your Windows PC, in every browser and in many apps.**

Pluton Shield is a free ad blocker for Windows 10 and 11 in the spirit of AdGuard. You install it once, turn on one switch, and ads, trackers, pop-ups and many annoying page elements disappear from Chrome, Edge, Brave, Opera and other browsers at the same time. You don't need a browser extension.

[**Download the latest version**](https://github.com/plutonlabs/shield/releases/latest)

> **Early release.** Pluton Shield is new. It works, but expect rough edges, and please report what you find on the [issues page](https://github.com/plutonlabs/shield/issues).

---

## What it does

- **Blocks ads and trackers** for all your browsers at once, using more than 60 community filter lists (EasyList, uBlock Origin filters, AdGuard lists, lists for many countries and more).
- **Hides leftover ad boxes**: empty frames, sponsored blocks and banners that normal blocking leaves behind.
- **Stops advertising pop-up windows.**
- **Removes tracking tags** from web addresses.
- **Can block adult websites** (off by default) and keep SafeSearch on in Google, Bing, DuckDuckGo and YouTube.
- **Lets you choose**: turn individual filter lists on or off, write your own rules, exclude sites you never want filtered, and leave individual programs alone.
- **Updates itself.** Filters refresh every few hours, and the app tells you when a new version of Pluton Shield is ready.
- **Runs quietly** in the system tray and can start with Windows.
- **Speaks English and Vietnamese** (it follows your Windows language, and you can change it in Settings).

## Install

1. Open the [Releases page](https://github.com/plutonlabs/shield/releases/latest) and download **`PlutonShield-v…-win-x64-Setup.exe`**.
   Use the `arm64` file only if your PC has an ARM chip (for example a Snapdragon laptop). If you are not sure, you want `x64`.
2. Run it. It installs just for you, so it does **not** ask for administrator rights.
3. Windows SmartScreen may say *"Windows protected your PC"*, because the installer is not code-signed yet. Click **More info**, then **Run anyway**.
4. Open Pluton Shield, press the big switch on the Home page, and confirm the certificate prompt (see [Why does it need a certificate?](#why-does-it-need-a-certificate)).

That's it. Restart your browser if it was open, and visit a page that normally has ads.

Windows 10 (version 1809 or later) and Windows 11 are supported. You don't need to install .NET or anything else.

## Using Pluton Shield

| Page | What you can do there |
|---|---|
| **Home** | Turn protection on or off, see how much has been blocked, and press **Test protection** to check that everything works. |
| **Filters** | See the filter version and update the filters right now. |
| **Filter lists** | Turn individual lists on or off, search them, switch whole groups (ads, privacy, security, annoyances, countries), and turn on the adult-content filter. |
| **Apps** | See the programs that connect through Pluton Shield and switch any of them off, so that Pluton Shield leaves that program completely alone. |
| **My filters** | Write your own rules, and try an address to see whether it would be blocked. |
| **Statistics** | See the most blocked sites and recent activity. |
| **Settings** | Language, start with Windows, pop-up blocking, sites that are never filtered, the certificate, updates, and troubleshooting tools. |

Closing the window keeps Pluton Shield running in the system tray. Right-click the tray icon to open it, turn protection off or on, update the filters, or quit. Your settings are saved the moment you change them.

### Keeping it up to date

When a new version of Pluton Shield is released, a **bell with the message "A new update is available."** appears above *Settings*. Click it to read what's new, then press **Update now**. The app downloads the installer, checks it, closes itself, updates and opens again. Nothing is installed until you press the button.

You can also press **Check for updates** at the top of *Settings*, or turn off automatic checks there.

### Blocking adult websites

At the top of *Filter lists* there is a switch, **Block adult content**. It is **off** until you turn it on. When you do, Pluton Shield downloads one extra list (the [HaGeZi NSFW list](https://github.com/hagezi/dns-blocklists), tens of thousands of sites) and shows a page that says *"Adult website blocked"* instead of the site. Nothing is downloaded and nothing is blocked while the switch is off.

- **Keep SafeSearch on** (also on by default once the filter is on) makes Google, Bing, DuckDuckGo and YouTube use their safe mode, so adult results are left out of the searches you do there. YouTube is set to "Moderate".
- There is **no password**: anyone using the PC can switch it off again. If you want to stop someone else from doing that, this is not the tool for it.
- It needs protection to be **on**, and **HTTPS filtering** (the default) for the explanation page and for SafeSearch. With HTTPS filtering off, the connection to an adult site is simply refused and the browser shows an error.
- It is a list, so it is not perfect: new or unlisted sites get through, and it does not stop a VPN, Tor, or a browser set to ignore the Windows proxy. It covers the Windows account it is installed in.
- A site you added to *Sites that are never filtered* is never blocked by this filter.

### Leaving a program alone

The *Apps* page lists the programs that have connected to the internet through Pluton Shield since it started (your browser, Discord, a game launcher...). Each one has a switch. Turn it **off** and Pluton Shield leaves that program completely alone: its HTTPS connections are not opened, nothing is blocked, and SafeSearch is not applied. Use it when one program breaks, or refuses to connect, because of the filter, and you still want everything else filtered.

- A program shows up in the list once it has used the network. If you cannot see it, open it, let it load something, and look again. **Add a program…** lets you pick its `.exe` file instead, even if it is not running.
- A program is recognised by its **file name** (for example `discord.exe`), not by the folder it is in. Its switch therefore keeps working when the program updates itself into a new folder, and it also applies to any other program with the same file name.
- **Filter all again** puts every program back on the filter.
- Programs that do not use the Windows proxy are not filtered in the first place, so they are not listed. Switching off a *browser* means that browser is no longer filtered at all.
- A change counts from the next request, but a secure connection that a program already has open keeps the way it was opened until it closes.
- A program that runs as another user, or as administrator, may not be recognised. It is then filtered as usual.

### Writing your own rules

*My filters* accepts the same simple rules used by AdBlock Plus and uBlock Origin. One rule per line, and lines starting with `!` are notes:

```
||ads.example.com^                      block a whole site or ad server
example.com##.banner                    hide an element on one site
example.com##div:has-text(Sponsored)    hide a block that contains the word "Sponsored"
@@||example.org^$document               never filter this site
```

## Why does it need a certificate?

Almost every website uses HTTPS, which is encrypted. To remove ads from an encrypted page, Pluton Shield has to open it on your computer first, in the same way antivirus programs and AdGuard do. For that, it creates its own certificate **on your PC** and asks Windows to trust it, and Windows asks you to confirm.

- The certificate and its private key are created on your computer, and the key never leaves it. It is stored protected for your Windows account only.
- Banking sites, Microsoft sign-in and similar sites are skipped automatically, and you can add more sites that should never be filtered in *Settings*.
- Uninstalling Pluton Shield removes the certificate again.

## Privacy

Pluton Shield has no accounts, no ads and no tracking of its own. It does not send your browsing history anywhere. Everything is filtered on your computer, and the statistics you see never leave it.

The only connections the app makes itself are to GitHub: to download filter lists (and the adult-content list, only if you turn that on), and to check for a new version of Pluton Shield.

## Troubleshooting

**The internet stopped working after Pluton Shield closed unexpectedly.**
Pluton Shield works by setting a local proxy in Windows, and it gives your original setting back when it closes. After a crash or a power cut that can be missed. Just open Pluton Shield again, which restores it, or go to *Settings → Troubleshooting → Restore the Windows proxy setting*.

**A website looks broken or won't load.**
Turn protection off from the tray icon to see if Pluton Shield is the cause. If it is, add the site to *Sites that are never filtered* in *Settings*, or look in *Filter lists* for a list that is too aggressive (the ones marked "Strong" block the most).

**Ads still show up.**
Make sure protection is on, then press **Test protection** on the Home page. It checks the filters, the certificate and the Windows settings and tells you what is wrong. Pressing **Update now** on the Filters page can also help.

**Firefox shows certificate warnings.**
Firefox keeps its own list of trusted certificates. In *Settings*, press **Open certificate folder (for Firefox)** and import the file in Firefox, or set `security.enterprise_roots.enabled` to `true` in `about:config`.

**An app complains about a certificate or can't connect.**
A few apps refuse to work with any ad blocker that inspects HTTPS. Add their address to *Sites that are never filtered*, or switch the whole program off on the *Apps* page.

**Start with Windows doesn't work.**
Open *Settings* and make sure *Start with Windows* is on. Also check that Pluton Shield isn't switched off under *Windows Settings → Apps → Startup*. If it still doesn't start, press *Copy diagnostics report* in *Settings → Troubleshooting* and include it when you ask for help.

**Asking for help.** Press *Copy diagnostics report* in *Settings → Troubleshooting*. The report has the version and status but no passwords and none of your own rules. Please read it, then paste it into a [new issue](https://github.com/plutonlabs/shield/issues).

## Uninstall

Go to *Windows Settings → Apps → Installed apps*, find **Pluton Shield** and uninstall it. The uninstaller closes the app, restores your Windows proxy setting, removes the Pluton Shield certificate (Windows asks you to confirm) and asks whether to delete your settings too.

## Good to know

- **SafeSearch** is best effort: it works by asking each search engine for its safe mode, and a search engine can change how that works.
- **Pop-up blocking** is best effort. It stops most advertising pop-ups, but not every one.
- **Firefox** needs the extra certificate step described above. Chrome, Edge, Brave and Opera work with the Windows certificate.
- If your network *requires* a company proxy, Pluton Shield's own updates may not be able to connect.
- The certificate lasts about 13 months. When it expires, Pluton Shield makes a new one and asks you to confirm it again.
- It isn't a Windows service yet: protection is active while Pluton Shield is running (it sits in the tray).

## Built on

Pluton Shield is made by [Pluton Labs](https://github.com/plutonlabs) and stands on these projects:

- [**neat-adblock**](https://github.com/plutonlabs/neat-adblock), the filtering engine, built on Brave's adblock-rust.
- [**neat-filters**](https://github.com/plutonlabs/neat-filters), the filter lists, collected from community projects and published automatically. The adult-content list is HaGeZi's [NSFW list](https://github.com/hagezi/dns-blocklists) (GPL-3.0), passed on through neat-filters.
- [**Titanium.Web.Proxy**](https://github.com/justcoding121/Titanium-Web-Proxy), the local proxy.

Thanks to everyone who maintains the open filter lists. They do the hard part.

## For developers

How it works inside, how to build it, how releases are made and what has been tested: see [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) (in Vietnamese).
