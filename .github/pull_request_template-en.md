## Summary

中文模板：[pull_request_template.md](pull_request_template.md)

## Changes


## Verification

- [ ] `dotnet build .\ServicePilot\ServicePilot.csproj -c Release`
- [ ] System tray behavior checked
- [ ] Log window behavior checked
- [ ] CLI / AI command behavior checked, or marked not applicable

## Safety Notes

- [ ] This change does not introduce command injection or unsafe process handling.
- [ ] Script execution is properly sandboxed and validated.
- [ ] Config compatibility is preserved or the migration is documented.
