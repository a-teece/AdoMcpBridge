# ADO MCP Bridge

A C# service that wraps Microsoft's Remote Azure DevOps MCP Server so
that Claude Code, Claude Desktop, and other MCP clients can use it
today, by acting as an OAuth Authorization Server toward the client and
a static admin-consented OAuth client toward Entra.

Design spec: `docs/specs/2026-06-09-ado-mcp-bridge-design.md` (on the
initial design branch / PR).

Status: pre-implementation. See open PRs for the design spec.
