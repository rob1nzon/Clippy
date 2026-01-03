# Release Workflow Setup Instructions

## Problem
The current release only contains source code and no build artifacts (MSIX packages).

## Solution
I've created a new release workflow that automatically builds and uploads MSIX packages for all platforms (x64, x86, arm64) when a release is created or a tag is pushed.

## Changes Made

### 1. New File: `.github/workflows/release.yml`
This workflow:
- Triggers on release creation, tag pushes, or manual dispatch
- Builds MSIX packages for x64, x86, and arm64 platforms
- Automatically uploads the packages to GitHub releases
- Uses Release configuration for optimized builds

### 2. Updated File: `.github/workflows/build.yml`
Simplified to focus on CI testing:
- Renamed to "CI Build"
- Runs on PRs and pushes to main/master
- Tests both Debug and Release configurations
- No longer attempts to create releases

## How to Apply These Changes

Since the GitHub App doesn't have permission to modify workflow files, please manually apply these changes:

### Option 1: Manual Application
1. Copy the contents of `.github/workflows/release.yml` from this branch
2. Create this file in your repository's main branch
3. Copy the updated `.github/workflows/build.yml` 
4. Replace the existing build.yml in your repository

### Option 2: Merge This Branch with Administrator Privileges
An administrator with workflow write permissions can:
```bash
git checkout main
git merge release-workflow-fix
git push origin main
```

## Testing the Release Workflow

After applying the changes:

### Option 1: Create a New Tag
```bash
git tag v1.0.0
git push origin v1.0.0
```

### Option 2: Manually Trigger
1. Go to Actions tab in GitHub
2. Select "Build and Release" workflow
3. Click "Run workflow"
4. Enter a tag name

### Option 3: Create a Release via GitHub UI
1. Go to Releases page
2. Click "Draft a new release"
3. Create a new tag
4. Publish the release

The workflow will automatically build and upload:
- `Clippy-x64.msix`
- `Clippy-x86.msix`
- `Clippy-arm64.msix`

## Updating the Existing "new" Release

To add build artifacts to the existing "new" release:

```bash
# Trigger the workflow manually for the "new" tag
gh workflow run release.yml --repo rob1nzon/Clippy --ref new -f tag=new
```

Or delete and recreate the release:
```bash
# Delete the old release
gh release delete new --repo rob1nzon/Clippy --yes

# Create a new one (the workflow will build and upload artifacts)
gh release create new --repo rob1nzon/Clippy --title "Clippy Release" --notes "Initial release with MSIX packages for all platforms"
```

## What Gets Built

Each platform build includes:
- MSIX package (unsigned, for sideloading)
- All dependencies bundled
- Optimized Release configuration
- Ready for installation on Windows 10/11

## Troubleshooting

If builds fail:
1. Check the Actions tab for detailed logs
2. Ensure all NuGet packages are accessible
3. Verify the Windows SDK version is available
4. Check that the Tray patching is working correctly

## Next Steps

1. Apply the workflow files to the main branch
2. Delete and recreate the "new" release to trigger builds
3. Verify that MSIX packages are uploaded
4. Test installation on target platforms
