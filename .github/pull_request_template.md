## Summary
-

## Azure / configuration
A separate Production json file **does not** skip deploying new C#. Check these if this PR should not change live App Service:

- [ ] I did **not** intend to change `appsettings.Production.json` or `.github/workflows/deploy-azure.yml`
- [ ] If I did change them, I added the label **`azure-config-ack`** and I know a deploy to **main** (or Run workflow) will update Azure
- [ ] No API keys or localhost-only secrets in files that are not `*.local.json`

## Test
- [ ] Local login / claims still work
- [ ] Azure not required for this PR
