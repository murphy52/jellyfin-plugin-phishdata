# 🚧 Development Workflow

## Branch Structure

- **`master`** - Production-ready releases only (v1.3.2, etc.)
- **`development`** - Active development and testing

## Development Testing

### For Developers/Testers:

1. **Add Development Repository** in Jellyfin:
   ```
   Repository Name: Phish Data Development
   Repository URL: https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishdata/development/manifest-dev.json
   ```

2. **Install "Phish Data (DEV)"** plugin from catalog
3. **Test features** and provide feedback
4. **Switch back to production** by:
   - Uninstalling "Phish Data (DEV)"
   - Installing "Phish Data" from main repository

### For Production Users:

**Use the main repository only:**
```
Repository URL: https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishdata/master/manifest.json
```

## Release Process

### Development Releases (v1.3.3-dev, v1.3.4-dev, etc.):
1. Make changes on `development` branch  
2. Update `manifest-dev.json` with new dev version
3. Create GitHub release with `-dev` tag
4. Test thoroughly

### Production Releases (v1.4.0, v1.4.1, etc.):
1. Merge tested features from `development` to `master`
2. Update main `manifest.json` with production version
3. Create GitHub release with production tag
4. Production users get stable update

## Benefits:
- ✅ **Production users** stay on stable releases
- ✅ **Developers** can test experimental features  
- ✅ **Clear separation** between dev and prod
- ✅ **Easy rollback** if dev version breaks
- ✅ **Faster iteration** without version number explosion

## Current Status:
- **Production**: v1.3.2 (stable, character encoding improvements)
- **Development**: v1.3.3-dev (experimental aggressive encoding fixes)