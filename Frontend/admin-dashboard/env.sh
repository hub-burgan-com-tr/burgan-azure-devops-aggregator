#!/bin/sh

# env.sh - Runtime environment configuration for React app

# Set default API base URL if not provided
API_BASE_URL=${API_BASE_URL:-"https://localhost:7232/api"}

echo "🔧 Configuring frontend with API_BASE_URL: $API_BASE_URL"

# Find all JavaScript files in the build directory
for file in /usr/share/nginx/html/static/js/*.js; do
    if [ -f "$file" ]; then
        echo "📝 Processing: $file"
        
        # Check if file is writable
        if [ -w "$file" ]; then
            # Replace the placeholder with actual environment variable
            sed -i "s|REACT_APP_API_BASE_PLACEHOLDER|$API_BASE_URL|g" "$file" 2>/dev/null || echo "⚠️  Could not modify $file"
        else
            # Make file writable first (OpenShift compatibility)
            chmod 644 "$file" 2>/dev/null || true
            if [ -w "$file" ]; then
                sed -i "s|REACT_APP_API_BASE_PLACEHOLDER|$API_BASE_URL|g" "$file" 2>/dev/null || echo "⚠️  Could not modify $file"
            else
                echo "⚠️  File not writable: $file (using default config)"
            fi
        fi
    fi
done

echo "✅ Environment configuration completed" 