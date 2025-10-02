#!/bin/bash

# XML Rules Auto Import Script - EXAMPLE VERSION
# Bu script git diff ile değişen JSON dosyalarını bulur ve API'ye gönderir.
# 🚨 Dikkat: Production ortamında gerçek API URL ve kimlik bilgilerini ENV ile giriniz.

set -e  # Exit on any error

# ============================
# Configuration (Example Only)
# ============================
API_BASE_URL="${API_BASE_URL:-https://example-api.com}"   # <- Burada kendi API URL'ini ENV'den geç
API_ENDPOINT="/RulesExecute/save"
RULES_DIR="${RULES_DIR:-Rules}"
PREVIOUS_COMMIT="${PREVIOUS_COMMIT:-HEAD~1}"
CURRENT_COMMIT="${CURRENT_COMMIT:-HEAD}"

# ============================
# Colored output (optional)
# ============================
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🚀 XML Rules Auto Import Script - EXAMPLE VERSION${NC}"
echo -e "${BLUE}===============================================\n${NC}"

# API URL kontrolü
if [[ -z "$API_BASE_URL" || "$API_BASE_URL" == "https://example-api.com" ]]; then
    echo -e "${RED}❌ ERROR: API_BASE_URL environment variable is not set${NC}"
    echo -e "${YELLOW}Please set: export API_BASE_URL=https://your-actual-api-url.com${NC}"
    exit 1
fi

CLEAN_BASE_URL="${API_BASE_URL%/}"
FULL_API_URL="${CLEAN_BASE_URL}${API_ENDPOINT}"

echo -e "${BLUE}📋 Configuration:${NC}"
echo -e "  API Base URL: $API_BASE_URL"
echo -e "  Full API URL: $FULL_API_URL"
echo -e "  Rules Directory: $RULES_DIR"
echo -e "  Previous Commit: $PREVIOUS_COMMIT"
echo -e "  Current Commit: $CURRENT_COMMIT\n"

# ==============================================
# Find changed JSON files (git diff veya fallback)
# ==============================================
echo -e "${BLUE}🔍 Finding changed JSON files...${NC}"
CHANGED_FILES=$(git diff --name-only --diff-filter=AMRC "$PREVIOUS_COMMIT" "$CURRENT_COMMIT" -- "$RULES_DIR/*.json" 2>/dev/null || true)

if [[ -z "$CHANGED_FILES" && -d "$RULES_DIR" ]]; then
    echo -e "${YELLOW}⚠️  No changed JSON files found via git diff. Checking all files...${NC}"
    CHANGED_FILES=$(find "$RULES_DIR" -name "*.json" -type f 2>/dev/null || true)
fi

if [[ -z "$CHANGED_FILES" ]]; then
    echo -e "${GREEN}✅ No JSON files to import.${NC}"
    exit 0
fi

echo -e "${GREEN}📁 Found files to process:${NC}"
echo "$CHANGED_FILES"
echo ""

# ==============================================
# Process files
# ==============================================
for file in $CHANGED_FILES; do
    echo -e "${BLUE}📤 Processing: $file${NC}"
    
    if ! jq empty "$file" 2>/dev/null; then
        echo -e "${RED}❌ Invalid JSON: $file${NC}"
        continue
    fi
    
    echo -e "${GREEN}  ✅ JSON validation passed${NC}"
    
    HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" \
        -X POST \
        -H "Content-Type: application/json" \
        -d @"$file" \
        "$FULL_API_URL" 2>/dev/null || echo "000")
    
    if [[ "$HTTP_STATUS" -eq 200 ]]; then
        echo -e "${GREEN}  ✅ Successfully imported: $file${NC}"
    else
        echo -e "${RED}  ❌ Failed to import: $file (HTTP $HTTP_STATUS)${NC}"
    fi
    echo ""
done

echo -e "${GREEN}🎉 Import script finished (example version).${NC}"