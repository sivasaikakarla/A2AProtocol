# MCP Client Configuration Guide

This guide details how to connect Model Context Protocol (MCP) clients—including **Claude Desktop**, **Cursor IDE**, and custom MCP client runners—to the **A2A Enterprise MCP Server**.

---

## 1. Architecture Overview

The MCP Server exposes enterprise tools via the Model Context Protocol (MCP) streamable HTTP transport at:
`http://localhost:5000/mcp` (or your production host URL).

Exposed MCP Tools:
- `search_financial_filings`: Semantic RAG vector similarity search over SEC 10-K filings using pgvector and grounded synthesis.
- `check_compliance`: Automated regulatory auditing against SOX-404, SEC-Risk-Disclosure, and Basel-III frameworks.
- `generate_audit_report`: Multi-section audit report generator (executive-summary, risk-analysis, full).
- `lookup_company`: Corporate metadata, CIK, industry, and indexed filing period lookup.
- `run_agent`: Autonomous multi-turn ReAct reasoning agent loop orchestrating all enterprise tools.

---

## 2. Claude Desktop Setup

To connect Claude Desktop to the A2A MCP Server:

1. Open Claude Desktop's configuration file:
   - **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
   - **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
   - **Linux**: `~/.config/Claude/claude_desktop_config.json`

2. Add the `a2a-orchestrator` MCP server configuration:

### Direct Streamable HTTP Transport:
```json
{
  "mcpServers": {
    "a2a-orchestrator": {
      "url": "http://localhost:5000/mcp",
      "headers": {
        "Authorization": "Bearer <YOUR_JWT_TOKEN>"
      }
    }
  }
}
```

### Stdio Bridge (via `mcp-proxy` or `curl`):
If your Claude Desktop client version requires stdio transport, you can use the official `mcp-proxy`:
```json
{
  "mcpServers": {
    "a2a-orchestrator": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-proxy",
        "http://localhost:5000/mcp"
      ]
    }
  }
}
```

---

## 3. Cursor IDE Setup

1. Open **Cursor Settings** -> **Features** -> **MCP**.
2. Click **+ Add New MCP Server**.
3. Configure the server:
   - **Name**: `A2A Enterprise Server`
   - **Type**: `command` or `sse` / `http`
   - **URL**: `http://localhost:5000/mcp`
4. Alternatively, in `.cursor/mcp.json`:
```json
{
  "mcpServers": {
    "a2a-enterprise-tools": {
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

---

## 4. Obtaining an Authentication Token

To generate a token for your client:
```bash
curl -X POST http://localhost:5000/auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "clientId": "claude-desktop-user",
    "roles": ["tool-executor", "admin"]
  }'
```

Response:
```json
{
  "token": "eyJhbGciOi...",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

Include the returned token in the `Authorization: Bearer <token>` header of your MCP client requests.
