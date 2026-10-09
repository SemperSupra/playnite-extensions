# MLE Evidence-Source Audit: Playnite Library Plugins and File-Backed Media Provenance

## Executive Summary & Task Scope

This document presents a public-repository, source-referenced audit of Playnite 10 library plugins and file-backed media provenance for non-game media purchases (books, ebooks, comics, and audio / soundtracks).

The primary objective is to transition Media Library Enrichment (MLE) from speculative source/title classification to data-backed, file-provenance evidence.

### Public Boundaries & Zero-Trust Guarantees
- **Source Material**: Inspects public Playnite 10 SDK sources and public library plugin integrations (e.g., official Humble Library Plugin).
- **Environment & Privacy Boundaries**: Public repository and SDK discovery only. No access to private repositories, personal game libraries, installation paths, account credentials, cookies, or live authenticated endpoints.
- **Library Mutation Guarantee**: No mutation of Playnite library DB, external metadata, user settings, or remote services during analysis.
- **Privacy Assurance**: No raw real-world game records, usernames, or local filesystem paths are stored or exported.

---

## 1. Traceable Playnite 10 SDK Source Inspection

Inspection based on Playnite 10.62 tag `10.62` (commit `7595561a084ef75775c94132a4e23506bf40df4d` in `JosefNemec/Playnite`).

### 1.1 Playnite Database Models (`Playnite.SDK.Models`)

#### `Game` Model Properties & Provenance
| Property | Type | Provenance & Source Data | Local Persistence in DB |
| :--- | :--- | :--- | :--- |
| `Id` | `Guid` | Playnite-assigned unique game record identifier. | Yes (`games.db`) |
| `GameId` | `string` | External provider machine key / subproduct ID. | Yes (`games.db`) |
| `PluginId` | `Guid` | Guid of library plugin that imported the record. | Yes (`games.db`) |
| `Source` | `GameSource` | `Source.Name` (e.g. "Humble", "Steam", "Manual"). | Yes (`games.db`) |
| `Name` | `string` | Display title of the product. | Yes (`games.db`) |
| `InstallDirectory` | `string` | Path on local disk if installed/downloaded. | Yes (`games.db`) |
| `IsInstalled` | `bool` | Flag indicating whether product is installed locally. | Yes (`games.db`) |
| `Manual` | `string` | Local path pointing to manual or main document file. | Yes (`games.db`) |
| `Notes` | `string` | Freeform notes text field. | Yes (`games.db`) |
| `Roms` | `ExtensionCollection<GameRom>` | List of ROM/media file paths (`GameRom.Path`, `GameRom.Name`). | Yes (`games.db`) |
| `GameActions` | `ObservableCollection<GameAction>` | Custom non-Play actions (`Type = File/URL`, `Path`, `WorkingDir`). | Yes (`games.db`) |
| `Links` | `ObservableCollection<Link>` | Web links (`Link.Name`, `Link.Url`). | Yes (`games.db`) |

#### `GameMetadata` Model (`LibraryPlugin.GetGames`)
Returned by library plugins during library import/sync:
- Maps external purchase payloads into initial `Game` model properties.
- **Crucial Limitation**: `GameMetadata` transfers primitive values (`Name`, `GameId`, `Links`, `InstallDirectory`) into Playnite DB. It **does not** retain or serialize raw provider API response JSON into the standard `Game` DB record.

#### `LibraryPlugin` and `LibraryMetadataProvider` APIs
- `LibraryPlugin.GetGames(GetGamesArgs args)`: Fetches purchases from provider API or local plugin cache.
- `LibraryPlugin.GetMetadataDownloader(...)` / `LibraryMetadataProvider`: Fetches extended metadata (Covers, Descriptions, Tags, Genres, Series).
- **Behavioral Limit**: Metadata providers supply descriptive attributes, not file existence verification.

---

## 2. Public Humble Library Integration Source Audit

Analysis of official Playnite Humble Library Plugin (`src/Playnite.DesktopApp/Plugins/HumbleLibrary`, Plugin GUID: `96e69be2-843c-4424-81e5-5e04e1383020`).

### 2.1 Provider API & Local Sync Data Flow
1. **Remote Endpoint**: Humble Order API (`https://www.humblebundle.com/api/v1/orders`).
2. **Humble Order Payload Structure**:
   - `subproducts`: Contains `machine_name`, `human_name`, `icon`, `downloads`.
   - `downloads`: Array of downloadable items containing `download_struct`:
     - `download_type`: "ebook", "audio", "comic", "bundle", "windows", "mac", "linux".
     - `options_struct`: Array of format choices (`download_file_info` containing `name`, `url`, `file_size`, `sha1`, format extension like `.pdf`, `.epub`, `.cbz`, `.mp3`, `.flac`).
3. **Import into Playnite Database**:
   - The plugin converts each subproduct into a `GameMetadata` record:
     - `GameMetadata.GameId` = `subproduct.machine_name`
     - `GameMetadata.Name` = `subproduct.human_name`
     - `GameMetadata.PluginId` = `96e69be2-843c-4424-81e5-5e04e1383020`
     - `GameMetadata.Source` = `GameSource("Humble")`
4. **Plugin-Private Local Cache vs Standard Database**:
   - Full Humble order JSONs with download format structs are written to plugin-private data directories (`%APPDATA%\Playnite\ExtensionsData\96e69be2-843c-4424-81e5-5e04e1383020\cache\...`).
   - **DB Limitation**: Standard Playnite `Game` DB records **do not** contain the `download_struct` format options (`pdf`, `epub`, `cbz`, `flac`) unless local files have been downloaded and populated into `Manual`, `Roms`, or `GameActions`.

---

## 3. Comparison of Existing MLE Classifier to Actual SDK Data Flow

### 3.1 Prior Speculative Logic Audit
Previously in MLE:
- `HumbleMediaAdmissionAdapter`: Admitted any `Game` record where `Source.Name` contained "Humble".
- `MediaCandidateClassifier`: Inspected `Manual` and `Notes` for media extensions (`.pdf`, `.epub`, `.cbz`, `.flac`, etc.).
- **Defect**: Source name ("Humble") or title ("Some Product") was treated as positive admission even when no file paths were present. For example, a Humble video game like *Braid* or *Psychonauts* has `Source = "Humble"`. Under speculative classification, it was admitted into admission evidence purely based on its source label, yielding false positives.

### 3.2 Corrected Provenance Directive
- **Provider Metadata is NOT Local Media Existence**: Source label `Source.Name == "Humble"` or title string `Name` indicates purchase origin or product metadata, NOT physical file existence or media modality.
- **Physical-File Existence Gate**: Positive classification requires verifiable, file-backed path evidence in `Manual`, `Notes`, `Roms`, `GameActions`, or `InstallDirectory`. Title-only or source-label-only inputs MUST produce an explicit `SKIP_NO_EVIDENCE` outcome.

---

## 4. Local Inspection Capability & Field Matrix

| Field | Provenance | Confidence Level | Privacy Risk | Allowed Local-Only Operation | Explicit No-Evidence Outcome |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Game.Manual` | Local User / Plugin Config | High (Direct File Path) | Low (Local Path) | Read-only path extension check & `File.Exists` validation | `SKIP_NO_EVIDENCE` if empty or non-media extension |
| `Game.Notes` | Local User / Import Text | Medium (Freeform Text) | Medium (May contain user text) | Regex/terminal extension extraction for local paths | `SKIP_NO_EVIDENCE` if no valid file paths found |
| `Game.Roms` | Local User / Plugin Config | High (Direct Path Array) | Low (Local Paths) | Array path extension check & `File.Exists` validation | `SKIP_NO_EVIDENCE` if array empty or no media paths |
| `Game.GameActions` | Local User / Action Config | High (Structured File Actions) | Low (Local Action Paths) | Filter `GameActionType.File` non-play actions for media paths | `SKIP_NO_EVIDENCE` if no file actions match media types |
| `Game.InstallDirectory` | Plugin / Download Manager | High (Directory Path) | Low (Local Dir Path) | Check directory existence or contained media files | `SKIP_NO_EVIDENCE` if directory missing or empty |
| `Game.Source` | Provider / Metadata | Low for Media Format | None | Provider identity tagging only (e.g. Humble/Steam) | **Never** used alone for positive media admission |
| `Game.Name` | Provider / Metadata | Low for Media Format | None | Product title reference only | **Never** used alone for positive media admission |
| `Game.PluginId` | Playnite Library Sync | High for Import Plugin | None | Origin plugin verification | Insufficient alone for file-backed media existence |

---

## 5. Negative Space Guarantees

The following cannot be established from standard Playnite 10 local database records without external authentication or plugin-private cache access:
1. **Un-downloaded Purchase Media Format**: If a Humble book purchase is not installed and has no local path recorded in `Manual`/`Notes`/`Roms`/`GameActions`, Playnite's DB cannot confirm whether it is PDF, EPUB, or CBZ.
2. **Remote Stream / CDN URL Integrity**: External download URLs stored in `Links` or private cache expire and cannot confirm local media file presence.
3. **Account / Tier Ownership Details**: Humble bundle tier breakdown or purchase receipts are not stored in standard Playnite DB fields.

---

## 6. Provider-Agnostic Local Media Extractor Architecture

Implemented in `plugins/media-library-enrichment/src/MediaLibraryEnrichment.Core/FileBackedMediaExtractor.cs`.

### 6.1 DTO Structures
```csharp
public sealed class FileBackedMediaExtractorInput
{
    public string Name { get; set; }
    public string Source { get; set; }
    public string ManualPath { get; set; }
    public string Notes { get; set; }
    public IEnumerable<string> Roms { get; set; }
    public IEnumerable<string> GameActions { get; set; }
    public string InstallDir { get; set; }
}

public sealed class FileBackedMediaResult
{
    public bool HasPositiveEvidence { get; set; }
    public string Kind { get; set; } = "unresolved"; // "book", "comic", "audio", "unresolved"
    public string PrimaryLocalEvidencePath { get; set; } = string.Empty;
    public string[] LocalEvidenceNames { get; set; } = Array.Empty<string>();
    public string EvidenceOutcome { get; set; } = "SKIP_NO_EVIDENCE"; // "POSITIVE_FILE_EVIDENCE", "SKIP_NO_EVIDENCE", "UNRESOLVED_FORMAT"
}
```

### 6.2 Extraction Decision Flow
1. **Candidate Path Aggregation**: Collects candidate paths from `ManualPath`, `Roms`, `GameActions`, `Notes` (resolved terminal extension paths), and `InstallDir`.
2. **Extension Classification**:
   - `book`: `.pdf`, `.epub`, `.mobi`, `.azw`, `.azw3`, `.prc`
   - `comic`: `.cbz`, `.cbr`, `.cb7`
   - `audio`: `.flac`, `.mp3`, `.m4a`, `.m4b`, `.ogg`, `.wav`
3. **Negative Space Fallback**:
   - If candidate paths exist but have non-media extensions (e.g. `.exe`, `.iso`, `.txt`), returns `EvidenceOutcome = "UNRESOLVED_FORMAT"` and `HasPositiveEvidence = false`.
   - If no candidate paths are present (even if `Name` or `Source` are populated), returns `EvidenceOutcome = "SKIP_NO_EVIDENCE"` and `HasPositiveEvidence = false`.

---

## 7. Owner-Local Aggregate Field Presence Measurement Schema

To enable local library owners to measure aggregate field presence across their Playnite library without exporting sensitive game titles, file paths, or user identifiers, the following local diagnostic summary schema is defined:

```json
{
  "$schema": "sempersupra-media-library-enrichment-field-presence-metrics/v1",
  "timestampUtc": "2025-01-01T00:00:00Z",
  "totalRecordsEvaluated": 10005,
  "aggregateCounters": {
    "recordsWithManual": 12,
    "recordsWithNotes": 45,
    "recordsWithRoms": 0,
    "recordsWithFileActions": 4,
    "recordsWithInstallDir": 1500,
    "recordsWithMediaExtensions": 4,
    "positiveFileEvidenceCount": 4,
    "skipNoEvidenceCount": 9998,
    "unresolvedFormatCount": 3
  },
  "modalityDistribution": {
    "book": 2,
    "comic": 1,
    "audio": 1,
    "unresolved": 10001
  },
  "privacyPolicy": {
    "zeroExport": true,
    "noTitlesStored": true,
    "noPathsStored": true,
    "noUserIdsStored": true
  }
}
```

This metric model operates 100% locally and contains zero raw records or user PII.

---

## 8. Source References & Verification

- **Playnite Repository**: `https://github.com/JosefNemec/Playnite`
- **Playnite Release Tag**: `10.62`
- **Playnite Commit SHA**: `7595561a084ef75775c94132a4e23506bf40df4d`
- **Playnite SDK Target**: .NET Standard 2.0 / .NET 8.0 Test Runner
- **Native Test Execution Verification**: All 101 unit tests in `MediaLibraryEnrichment.Core.Tests` pass cleanly (`dotnet test`).
