# Fart Online website

The public site where anyone can play the game in the browser or download it. **Vercel hosts it**, deployed
from this folder of the GitHub repo through the Vercel dashboard (no CLI). The online room servers stay on the
EC2 game server (`Backend/README.md`), because Vercel can't run long-lived game servers.

## What's here (all of it is deployed)

| Path | What |
|---|---|
| `index.html`, `css/`, `js/` | The page. No framework and no build step. |
| `assets/` | Trailer stills, background loop and trailers, fonts (SIL OFL), icons. |
| `play/` | The WebGL build (`D:\FOBuild\Web` without the BurstDebug folder). Its `index.html` has `<base href="/play/">` added, so it loads from `/play` with or without the slash. |
| `downloads/` | `FartOnline.apk`, `FartOnline-Windows.zip` and `info.json` (sizes and version; the page reads it). |
| `vercel.json` | Download headers, caching, and a rewrite of `/api/*` to the room service on EC2 (for the "Servers online" badge). |

`js/site.js` also handles **account emails**: Supabase sends people back to the site's root with the result after
`#`. The page shows "Email confirmed!" or explains an expired link and offers "Send me a new link" (Supabase
`/auth/v1/resend` with the public anon key), then clears the tokens from the address bar.

## First-time setup on Vercel (dashboard only)

1. Push the repo to GitHub.
2. On vercel.com: **Add New… > Project > Import** the `fart_online` repo (allow Vercel's GitHub app to see it).
3. In **Configure Project**:
   - **Root Directory:** `Website`
   - **Framework Preset:** Other
   - **Build Command:** leave empty (or turn the override on and leave it blank); **Output Directory:** `.`
4. **Deploy.** The site is at `https://<project-name>.vercel.app`.
5. Optional, to stop Unity-only commits from redeploying the site: **Settings > Git > Ignored Build Step**,
   choose "Only build if there are changes in a folder" and enter `Website`.

## Supabase settings that point at the site

**Authentication > URL Configuration**:
- **Site URL:** `https://<project-name>.vercel.app` (this is where confirmation links land; it was `http://localhost:3000`).
- **Redirect URLs:** add `https://<project-name>.vercel.app/**`.

## Updating the site after a new game build

1. In Unity, **Tools > Fart Online > Build All for Website** (Linux server, Windows, Android, WebGL).
2. Replace `play/` with the new `D:\FOBuild\Web` (no BurstDebug folder) and add `<base href="/play/">` after
   `<head>` in `play/index.html`.
3. Replace the files in `downloads/` (zip `D:\FOBuild\Windows` without the BurstDebug folder, top folder
   `FartOnline`) and update the sizes, version and date in `downloads/info.json`.
4. Commit and push. Vercel redeploys by itself.
5. Upload the new Linux server to EC2 too (`Backend/README.md`, "New server build").

## Limits and notes

- Vercel Hobby (free) is for non-commercial use and includes about 100 GB of transfer a month: roughly 1,800 APK
  downloads, or many more browser plays (the game is about 36 MB, cached after the first visit).
- The 100 MB Hobby upload limit applies to CLI deploys only; Git deploys have no stated output limit.
- GitHub rejects files over 100 MB. The APK and the zip are about 55 MB each, so every new release adds ~150 MB to
  the repo's history. If the repo gets too big, move the downloads to GitHub Releases on a public repo or to a CDN.
- The Windows build isn't code-signed (SmartScreen warns once); the APK is signed with Unity's debug key.
