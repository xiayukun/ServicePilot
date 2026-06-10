# Screenshot Guide

[中文](screenshot-guide.md)

Use this checklist before the first public release to prepare README, GitHub Release, and social preview screenshots.

## Before Capturing

- Prepare 4 to 6 demo services with realistic local-dev names such as `screen`, `web`, `app`, `h5`, and `order-web`.
- Keep at least 1 service running and 1 service stopped. Optionally keep 1 failed example for diagnostics screenshots.
- Add 2 to 3 variables to a service, such as local, test, and dev API URLs.
- Prepare one full template with a config-editing step and an `npm run dev` startup step.
- Remove sensitive paths, tokens, private domains, and customer names. Use public example values when a URL must be visible.
- Capture key screenshots in both Chinese and English. Tray path: `语言` / `Language`.

## Required Screenshots

1. Tray context menu  
   Show the numeric tray icon, service list, status dots, start/run-step/log/edit/template actions, and the language switcher.

2. Service manager window  
   Show multiple services, runtime state, step count, variable count, and start, run-step, stop, restart, log actions.

3. Add or edit service window  
   Show service name, working directory, multi-step scripts, `Use variable`, `Run on start`, preset/manual step variables, and apply-template button.

4. Log window  
   Show live logs, search, copy menu, horizontal scrolling for long lines, and start/run-step/stop/restart/edit buttons.

5. Template manager window  
   Show full service templates, descriptions, step counts, variable counts, and template preview.

6. CLI / AI usage screenshot  
   Show these commands in a terminal:

   ```powershell
   ServicePilot.exe ai-help
   ServicePilot.exe doctor --json
   ServicePilot.exe status all --json
   ```

## Current Organized Screenshots

- `Assets/app-preview.png`: README hero image, currently using the service manager window.
- `Assets/screenshots/tray-menu-zh.png`: tray context menu.
- `Assets/screenshots/service-manager-zh.png`: service manager window.
- `Assets/screenshots/service-editor-zh.png`: service editor and script steps.
- `Assets/screenshots/log-window-zh.png`: live log window.
- `Assets/screenshots/ai-help-cli-zh.png`: `ai-help` command.
- `Assets/screenshots/status-doctor-cli-zh.png`: `status all` and `doctor --json`.

## Optional Screenshots

- Variable submenu showing recently used ordering and the `Add` entry.
- Run-step submenu showing `Startup steps` and `Manual steps` groups.
- Failure notification showing a tray balloon and the matching log summary.
- GitHub Release download page showing the `ServicePilot.exe` artifact.

## Size Suggestions

- README hero image: 1200 to 1600 px wide, preferably service manager or tray menu composition.
- Release image: 1000 to 1400 px wide, emphasizing log window and CLI.
- Social preview: 1280 x 640 px, using service manager + tray menu without too much text.

## Visual Requirements

- Do not show real internal addresses, customer names, tokens, or personal paths.
- Keep Windows scaling at 100% or 125% so text remains sharp.
- Tray-menu screenshots should include the notification-area number icon.
- English screenshots must confirm button text is not truncated.
- Use Chinese screenshots for `README.md` and English screenshots for `README-en.md`.
