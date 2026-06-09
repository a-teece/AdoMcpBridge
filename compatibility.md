# Compatibility Matrix

| Bridge version | Microsoft Remote MCP API generation | Notes |
| --- | --- | --- |
| v0.1.0 | API generation 1 (2026-Q2 baseline) | First public release. Tracks `https://mcp.dev.azure.com/{org}` as documented at https://learn.microsoft.com/en-us/azure/devops/mcp-server/remote-mcp-server. |

When MS ships a breaking change to the Remote MCP server we bump the
bridge **minor** version and add a new row. Patch releases never change
the API generation.
