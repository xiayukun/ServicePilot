# Privacy

中文：[PRIVACY.md](PRIVACY.md)

ServicePilot is designed as a local Windows utility.

## Data It Reads

ServicePilot reads:

- the working directories configured by the user
- script content entered in the configuration editor
- process output (stdout/stderr) from running services

## Data It Writes

ServicePilot writes:

- `%APPDATA%/ServicePilot/config.json`
- temporary script files in `%TEMP%/ServicePilot/`

## Network Access

ServicePilot does not require network access for its own operation. Services configured by the user may access the network as needed (e.g., `npm run dev` starts a dev server).

It does not upload files, paths, logs, configuration, or machine names to a remote service.

## Sensitive Paths

Configuration may contain local paths and script content. Treat `config.json` and log output as potentially sensitive before sharing them publicly.
