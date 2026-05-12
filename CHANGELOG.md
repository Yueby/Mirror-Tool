# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-05-13
### Added
- Added component property sync from source objects to mirror targets, configurable in the detail window and settings drawer
- Added Mirror Reference marking from the GameObject context menu for reference-based mirror planes
- Added hierarchy icons, highlights, and reference labels for mirror-connected objects and mirror references
- Added multi-selection mirror application support

### Changed
- Refactored Mirror Tool into partial editor modules for UI, mirroring, persistence, hierarchy, scene GUI, target list, and component sync
- Improved mirror config persistence with GlobalObjectId-backed mirror reference data, legacy path migration, normalization, and cache invalidation
- Improved mirror target list status display to show resolved mirror reference or world-space fallback state
- Improved mirror axis updates so connected objects stay synchronized

### Fixed
- Prevented self-mirror target assignments when editing or dragging targets
- Improved mirrored rotation fallback handling for degenerate reflected vectors

## [1.0.2] - 2025-11-02
### Added
- Added window position memory using EditorPrefs

## [1.0.1] - 2024-01-31
### Added
- Added confirmation dialog when adding objects via drag and drop
- Added local space visualization for scale handles
- Added detailed documentation and usage examples in README
- Added demo GIFs showing basic usage, advanced features and practical examples

### Fixed
- Fixed scale handle visualization in different coordinate spaces

## [1.0.0] - 2024-01-31
### Added
- Initial release of the package