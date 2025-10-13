#!/bin/bash
set -e

# Jellyfin Phish.net Plugin Build Script
# Builds and packages the plugin for distribution

VERSION="1.1.8"
PLUGIN_NAME="jellyfin-plugin-phishnet"
PROJECT_DIR="Jellyfin.Plugin.PhishNet"
OUTPUT_DIR="releases"
BUILD_DIR="build"

echo "🎵 Building Jellyfin Phish.net Plugin v${VERSION}"

# Clean previous builds
echo "🧹 Cleaning previous builds..."
rm -rf "$OUTPUT_DIR" "$BUILD_DIR"
mkdir -p "$OUTPUT_DIR" "$BUILD_DIR"

# Build the plugin
echo "🔨 Building plugin..."
dotnet build "$PROJECT_DIR" -c Release -o "$BUILD_DIR"

# Verify required files exist
REQUIRED_FILES=(
    "$BUILD_DIR/Jellyfin.Plugin.PhishNet.dll"
    "$BUILD_DIR/Jellyfin.Plugin.PhishNet.pdb"
)

echo "✅ Verifying build artifacts..."
for file in "${REQUIRED_FILES[@]}"; do
    if [[ ! -f "$file" ]]; then
        echo "❌ Missing required file: $file"
        exit 1
    fi
    echo "   ✓ Found: $(basename "$file")"
done

# Create the plugin package
PACKAGE_NAME="${PLUGIN_NAME}-${VERSION}.zip"
echo "📦 Creating plugin package: $PACKAGE_NAME"

cd "$BUILD_DIR"
zip -r "../$OUTPUT_DIR/$PACKAGE_NAME" \
    Jellyfin.Plugin.PhishNet.dll \
    Jellyfin.Plugin.PhishNet.pdb \
    -x "*.deps.json" "*.runtimeconfig.json" "*Microsoft*" "*System*"

cd ..

# Generate MD5 checksum
echo "🔐 Generating checksum..."
if command -v md5sum >/dev/null 2>&1; then
    CHECKSUM=$(md5sum "$OUTPUT_DIR/$PACKAGE_NAME" | cut -d' ' -f1)
elif command -v md5 >/dev/null 2>&1; then
    CHECKSUM=$(md5 -q "$OUTPUT_DIR/$PACKAGE_NAME")
else
    echo "⚠️  MD5 command not found, please calculate checksum manually"
    CHECKSUM="CALCULATE_MANUALLY"
fi

# Package info
PACKAGE_SIZE=$(ls -lh "$OUTPUT_DIR/$PACKAGE_NAME" | awk '{print $5}')

echo ""
echo "✅ Plugin build complete!"
echo "📁 Package: $OUTPUT_DIR/$PACKAGE_NAME"
echo "📏 Size: $PACKAGE_SIZE"
echo "🔐 MD5: $CHECKSUM"
echo ""
echo "📋 Release Information:"
echo "   Version: $VERSION"
echo "   Target ABI: 10.8.0.0+"
echo "   Package: $PACKAGE_NAME"
echo "   Checksum: $CHECKSUM"
echo ""
echo "🚀 Ready for release!"
echo "   1. Upload $OUTPUT_DIR/$PACKAGE_NAME to GitHub releases"
echo "   2. Update manifest.json with the correct checksum: $CHECKSUM"
echo "   3. Commit and push the updated manifest"

# Create a release notes template
cat > "$OUTPUT_DIR/release-notes-v${VERSION}.md" << EOF
# Jellyfin Phish.net Plugin v${VERSION}

## 🎵 Major Feature Release

### New Features
- **Community Ratings**: Aggregate ratings from up to 50 Phish.net reviews (1-10 scale)
- **Comprehensive Image Provider**: Multi-source image fetching with venue photos and show-specific images
- **Person Provider**: Individual profiles for all four band members with detailed biographies
- **Enhanced Metadata**: Smart titles, multi-night run detection, comprehensive tagging
- **Advanced Parsing**: Improved filename parsing with support for various naming conventions

### Technical Improvements
- Complete service architecture with modular design
- Comprehensive testing suite with mocked API responses
- Enhanced error handling and logging
- Performance optimizations and caching

### Installation

#### Via Plugin Catalog (Recommended)
1. In Jellyfin, go to **Admin Dashboard** → **Plugins** → **Repositories**
2. Add repository URL: \`https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishnet/master/manifest.json\`
3. Go to **Catalog** and install "Phish.net" plugin
4. Restart Jellyfin Server

#### Manual Installation
1. Download \`${PACKAGE_NAME}\`
2. Extract to Jellyfin's \`plugins/\` directory
3. Restart Jellyfin Server

### Package Information
- **Version**: ${VERSION}
- **Target Jellyfin**: 10.8.0+
- **Package Size**: ${PACKAGE_SIZE}
- **Checksum (MD5)**: \`${CHECKSUM}\`

### Documentation
- [Installation Guide](https://github.com/murphy52/jellyfin-plugin-phishnet/blob/master/INSTALLATION.md)
- [Configuration Guide](https://github.com/murphy52/jellyfin-plugin-phishnet/blob/master/README.md#configuration)
- [Troubleshooting](https://github.com/murphy52/jellyfin-plugin-phishnet/blob/master/README.md#troubleshooting)
EOF

echo "📝 Release notes created: $OUTPUT_DIR/release-notes-v${VERSION}.md"