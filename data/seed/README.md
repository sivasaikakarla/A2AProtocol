# SEC 10-K Financial RAG Knowledge Base

This module implements an automated data ingestion pipeline that fetches regulatory SEC 10-K filings directly from the SEC EDGAR system, segments filings into standardized financial sections, performs sliding-window chunking, computes 1024-dimensional semantic embeddings using NVIDIA NIM, and ingests them into PostgreSQL with `pgvector`.

---

## Companies Indexed

| Ticker | Company Name | Sector | Industry | SEC CIK |
|:-------|:-------------|:-------|:---------|:--------|
| **AAPL** | Apple Inc. | Technology | Consumer Electronics | `0000320193` |
| **MSFT** | Microsoft Corporation | Technology | Software & Cloud Computing | `0000789019` |
| **JPM** | JPMorgan Chase & Co. | Financial Services | Diversified Banking | `0000019617` |
| **TSLA** | Tesla, Inc. | Consumer Cyclical | Automotive & Clean Energy | `0001318605` |
| **GOOGL** | Alphabet Inc. | Communication Services | Internet Content & AI | `0001652044` |

---

## Pipeline Architecture

```
SEC EDGAR API (data.sec.gov)
         │
         ▼
HTML / Text Extraction (BeautifulSoup4)
         │
         ▼
Regulatory Section Parser (Items 1, 1A, 7, 8, 9A)
         │
         ▼
Sliding Window Chunker (~512 tokens / ~1800 chars, 50-token overlap)
         │
         ▼
NVIDIA NIM Embeddings (nvidia/nemotron-3.5-embed-1b @ 1024-dim)
         │
         ▼
PostgreSQL 16 + pgvector (document_chunks with IVFFlat Cosine Index)
```

---

## Running the Ingestion Pipeline

### Prerequisites
- Python 3.10+
- Running PostgreSQL container with pgvector (`docker-compose up -d postgres`)
- NVIDIA API Key configured in root `.env`

### Execution
```bash
# 1. Install dependencies
pip install -r data/scripts/requirements.txt

# 2. Run ingestion
python3 data/scripts/ingest_documents.py
```

---

## Verification Queries

Check total indexed chunks:
```sql
SELECT ticker, COUNT(*) AS chunk_count, MIN(fiscal_year) AS min_year, MAX(fiscal_year) AS max_year
FROM document_chunks
GROUP BY ticker;
```

Check semantic vector search:
```sql
SELECT ticker, section, content, 1 - (embedding <=> '[...query vector...]') AS similarity
FROM document_chunks
ORDER BY embedding <=> '[...query vector...]'
LIMIT 5;
```
