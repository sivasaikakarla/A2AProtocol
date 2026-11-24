# System Architecture & Technical Specifications

This document outlines the architecture, engineering principles, design choices, and security considerations of the **A2A Enterprise Agentic AI & Model Context Protocol (MCP) Orchestrator**.

---

## 1. High-Level Architecture Overview

The platform is designed around a **Vertical Slice Architecture** built upon **.NET 10** and **FastEndpoints**. It integrates high-throughput REST endpoints, standard Model Context Protocol (MCP) streamable HTTP transports, PostgreSQL with pgvector for semantic retrieval, Redis for AES-256-GCM encrypted token caching, and NVIDIA NIM for enterprise LLM reasoning and embeddings.

```mermaid
graph TD
    User["Client / MCP Consumer<br/>(Claude Desktop / Cursor / REST)"] -->|JWT Bearer / Streamable HTTP| Gateway["ASP.NET Core / FastEndpoints Host<br/>(:8080 or :5000)"]
    
    subgraph Security Layer
        Gateway --> Middleware["JwtMiddleware<br/>(Path Exclusion & Validation)"]
        Middleware --> TokenCache["EncryptedTokenCache<br/>(AES-256-GCM)"]
        TokenCache --> Redis[("Redis 7 Cache<br/>(:6380)")]
    end
    
    subgraph Agentic Orchestration Layer
        Gateway --> Orchestrator["AgentOrchestrator<br/>(ReAct Loop - Max 10 Turns)"]
        Orchestrator --> Registry["ToolRegistry<br/>(Schemas, Descriptions, Dispatch)"]
        Orchestrator --> NvidiaChat["NvidiaLlmClient<br/>(Nemotron Chat / Reasoning)"]
    end
    
    subgraph Enterprise MCP Tools
        Registry --> T1["search_financial_filings<br/>(RAG Vector Search)"]
        Registry --> T2["check_compliance<br/>(Rules Engine + LLM)"]
        Registry --> T3["generate_audit_report<br/>(Multi-Section Synthesis)"]
        Registry --> T4["lookup_company<br/>(Metadata & Filing Stats)"]
    end
    
    subgraph Data & Storage Layer
        T1 --> PgVectorService["VectorSearchService<br/>(Cosine Similarity)"]
        PgVectorService --> Postgres[("PostgreSQL 16 + pgvector<br/>(:5432)")]
        T2 --> Postgres
        T3 --> Postgres
        T4 --> Postgres
        Gateway --> AuditLogger["AuditLogService"]
        AuditLogger --> Postgres
    end
```

---

## 2. Core Architectural Decisions

### 2.1 Why FastEndpoints (vs. Minimal APIs or MVC Controllers)?
- **Vertical Slice Architecture**: FastEndpoints encapsulates Request, Handler, Response, and Validator within cohesive single-file slices rather than scattering logic across controllers, services, and DTO folders.
- **Performance & Zero Reflection Overhead**: FastEndpoints compiles route handlers and model bindings down to expressions, minimizing reflection and memory allocations per request.
- **Native MCP Extension**: Direct support for `FastEndpoints.Mcp`, allowing endpoints to seamlessly register as MCP tools via `this.McpTool(...)` without requiring duplicate server harnesses.
- **Standardized RFC 7807 Problem Details**: Built-in 422 Unprocessable Entity and Problem Details formatting out of the box with zero boilerplate.

### 2.2 Why pgvector & PostgreSQL (vs. Standalone Vector Databases like Pinecone/Milvus)?
- **ACID Compliance & Transactional Integrity**: Storing document embeddings in the same relational database as company metadata and immutable audit logs ensures atomic transactions and zero eventual consistency drift.
- **Relational Filtering with Vector Ranking**: pgvector enables hybrid queries combining relational SQL filters (e.g., `WHERE ticker = @t AND fiscal_year = @y`) with vector cosine distance (`ORDER BY embedding <=> @query_vec LIMIT @k`) in a single indexed query plan.
- **Cost & Operational Simplicity**: Eliminates the operational overhead, network latency, and billing tiers of third-party cloud vector DBs.

### 2.3 Why AES-256-GCM Encrypted Token Caching with Redis?
- **Authenticated Encryption with Associated Data (AEAD)**: AES-256 in Galois/Counter Mode (GCM) guarantees both confidentiality and integrity via a 128-bit authentication tag.
- **Defense in Depth**: Even if Redis memory or snapshots are intercepted, cached authorization tokens cannot be inspected, tampered with, or replayed without detection.
- **Hardware Acceleration**: .NET `System.Security.Cryptography.AesGcm` utilizes CPU-level AES-NI instructions, achieving sub-microsecond encryption and decryption throughput.

### 2.4 Why NVIDIA NIM?
- **Enterprise-Grade Latency & Self-Hostability**: NVIDIA Inference Microservices (NIM) offer optimized TensorRT-LLM execution with high-throughput batching.
- **State-of-the-Art Reasoning**: Utilizes models like `nvidia/nemotron-3.5-super-vl-120b-a12b` and `nvidia/nemotron-3.5-embed-1b` for grounded financial analysis and accurate semantic representation.
- **Resilient Fallback**: The client architecture gracefully fails over to secondary models on transient 429 rate limits.

---

## 3. Autonomous ReAct Agent Loop

The agent loop executes multi-step reasoning tasks autonomously:

```mermaid
sequenceDiagram
    autonumber
    actor Client as Client / MCP Client
    participant API as RunAgentEndpoint
    participant Agent as AgentOrchestrator
    participant LLM as NvidiaLlmClient (NIM)
    participant Reg as ToolRegistry
    participant Tool as Enterprise Tool / DB

    Client->>API: POST /api/agent/run {"request": "..."}
    API->>Agent: RunAsync(request, maxIterations)
    loop Up to 10 Iterations
        Agent->>LLM: ChatMessagesAsync(History + Tool Schemas)
        LLM-->>Agent: Response (Reasoning + ```tool_call``` or Final Answer)
        alt Response contains ```tool_call```
            Agent->>Reg: ExecuteToolAsync(toolName, arguments)
            Reg->>Tool: Execute in-process with scoped DI
            Tool-->>Reg: Output Data / Problem Details
            Reg-->>Agent: Observation JSON
            Agent->>Agent: Append Step to ExecutionPlan & Context
        else Direct Response (No tool_call)
            Agent->>Agent: Extract FinalAnswer
        end
    end
    Agent-->>API: AgentResponse (FinalAnswer, ExecutionPlan, Iterations)
    API-->>Client: 200 OK (AgentResponse JSON)
```

---

## 4. Database Schema Design

The PostgreSQL database uses the `vector(1024)` data type and IVFFlat index:

```mermaid
erDiagram
    COMPANIES ||--o{ DOCUMENT_CHUNKS : "has filings"
    COMPANIES {
        string ticker PK "VARCHAR(10)"
        string company_name "VARCHAR(255)"
        string sector "VARCHAR(100)"
        string industry "VARCHAR(100)"
        string cik "VARCHAR(20)"
        string description "TEXT"
        timestamp created_at "TIMESTAMPTZ"
    }

    DOCUMENT_CHUNKS {
        bigint id PK "BIGSERIAL"
        string ticker FK "VARCHAR(10)"
        string filing_type "VARCHAR(20)"
        integer fiscal_year "INT"
        string section "VARCHAR(100)"
        integer chunk_index "INT"
        text content "TEXT"
        vector embedding "VECTOR(1024)"
        timestamp created_at "TIMESTAMPTZ"
    }

    AUDIT_LOGS {
        bigint id PK "BIGSERIAL"
        string tool_name "VARCHAR(100)"
        string client_id "VARCHAR(100)"
        jsonb request_payload "JSONB"
        jsonb response_payload "JSONB"
        integer duration_ms "INT"
        boolean success "BOOLEAN"
        string error_message "TEXT"
        timestamp executed_at "TIMESTAMPTZ"
    }
```

---

## 5. Security Architecture

1. **Authentication**: JWT Bearer with HMAC-SHA256 signatures, validated issuer, audience, and configurable expiry.
2. **Path Exclusion**: `JwtMiddleware` inspects request path before authentication handlers. Safe paths (`/health`, `/auth/token`, `/mcp`, `/`) pass through without Bearer tokens.
3. **Role-Based Access Control (RBAC)**: All tool endpoints enforce `Roles("tool-executor", "admin")`.
4. **Input Validation**: FluentValidation rules enforce strict ticker regex (`^[A-Z]{1,10}$`), query length boundaries, and allowed enum values.
5. **Auditing**: Every tool invocation and agent step is immutably logged to `audit_logs` with execution duration, client identifier, and payload details.
