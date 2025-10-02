#!/bin/bash

# XML Rules Import Script - Example/Safe Version
# Bu sürümde gerçek URL yerine örnek endpoint kullanılır.
# Gerçek ortam değerlerini environment variable üzerinden geçmelisiniz.

set -e

# === CONFIG (Example) ===
API_BASE_URL="${API_BASE_URL:-https://example-api.com/api}"
RULES_DIR="${RULES_DIR:-Rules}"

# === Colors (optional) ===
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}🚀 XML Rules Special Import Script (Example)${NC}"
echo -e "${BLUE}===========================================${NC}\n"

# XML format (xmlLines içeren) JSON dosyaları tespit
XML_RULES=$(grep -l '"xmlLines"' "$RULES_DIR"/*.json 2>/dev/null || true)

if [[ -z "$XML_RULES" ]]; then
    echo -e "${YELLOW}No XML-format rules found in $RULES_DIR${NC}"
    exit 0
fi

echo -e "${BLUE}🎯 Found XML format rule files:${NC}"
for rule in $XML_RULES; do
    echo -e "  📄 $(basename "$rule")"
done

# Denenecek endpoint örnekleri (API uygulamanıza göre değiştirin)
ENDPOINTS=(
    "/RulesExecute/saveXml"
    "/RulesExecute/importXml"
    "/Rules/import"
    "/Rules/xml"
    "/RulesExecute/save"
)

echo -e "\n${BLUE}🔄 Trying endpoints for each file...${NC}"

for rule_file in $XML_RULES; do
    echo -e "\n${BLUE}Processing: $(basename "$rule_file")${NC}"

    # JSON içeriğini dizi formatına sarmaya çalış
    WRAPPED_CONTENT="[$(cat "$rule_file")]"

    for endpoint in "${ENDPOINTS[@]}"; do
        FULL_URL="${API_BASE_URL%/}$endpoint"
        echo -e "  🔗 Testing endpoint: $FULL_URL"

        HTTP_STATUS=$(curl -k -s -w "%{http_code}" -o response.tmp \
            -X POST \
            -H "Content-Type: application/json" \
            -d "$WRAPPED_CONTENT" \
            "$FULL_URL" 2>/dev/null || echo "000")

        if [[ "$HTTP_STATUS" -eq 200 ]]; then
            echo -e "${GREEN}    ✅ Success with endpoint: $endpoint${NC}"
            break
        else
            echo -e "${RED}    ❌ HTTP $HTTP_STATUS${NC}"
        fi
    done
done

rm -f response.tmp
echo -e "\n${GREEN}✅ Script finished (example version)${NC}"