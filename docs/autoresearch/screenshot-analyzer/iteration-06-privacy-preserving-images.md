# Iteration 6: Privacy-Preserving Image Processing for Student-Uploaded Photos

**Date**: 2026-04-12
**Series**: Screenshot Analyzer Security Research (6 of 10)
**Security Score Contribution**: +14 points (cumulative target: 72/100)
**Pipeline**: Student photo -> EXIF strip -> face blur -> crop -> Gemini 2.5 Flash -> LaTeX -> CAS validation

---

## Executive Summary

Cena's Path B screenshot analyzer allows students aged 14--18 to photograph
math and physics questions for AI-powered extraction. This creates a pipeline
where student-captured images -- potentially containing faces, GPS coordinates,
device fingerprints, and environmental context -- flow through the system.
This article establishes the legal framework, architectural patterns, and
implementation details required to process these images without retaining
personally identifiable information.

The core principle: **the image is a transient input, not a stored artifact.**
Only the extracted mathematical content (LaTeX strings, structured text) persists.
The image itself exists solely in volatile memory for the duration of processing,
then is irreversibly discarded.

---

## 1. Legal Framework

### 1.1 COPPA (Children's Online Privacy Protection Act)

**Applicability**: COPPA applies to operators of websites or online services
directed at children under 13, or operators with actual knowledge that they
collect personal information from children under 13. While Cena's primary user
base is 14--18, some users may be under 13, and the 2025 amended COPPA Rule
expands the definition of personal information.

**2025 Amended Rule (effective June 23, 2025; compliance deadline April 22, 2026)**:

The FTC finalized significant amendments to the COPPA Rule, published in the
Federal Register on April 22, 2025. Key changes relevant to image processing:

1. **Biometric data is now explicitly included** in the definition of "personal
   information." Biometric identifiers include fingerprints, retina patterns,
   voiceprints, gait patterns, facial templates, and faceprints. A student
   selfie containing a recognizable face is therefore personal information
   under the amended rule.

2. **Photos and videos** that contain a child's image constitute personal
   information when they can be used to identify the child.

3. **Data retention is strictly limited**: operators may retain children's
   personal information only "for as long as reasonably necessary to fulfill
   the specific purpose for which it was collected." Indefinite retention is
   explicitly prohibited.

4. **Written data retention policy required**: operators must create and
   maintain a written policy specifying the collection purpose, business need
   for retention, and a deletion timeline. This policy must be included in the
   privacy notice.

**Implication for Cena**: Student-uploaded photos must not be stored beyond
the processing window. The image is collected for one purpose (math extraction),
and once that purpose is fulfilled (LaTeX output returned), the image must be
deleted. No persistent storage of the image is permitted.

Sources:
- [FTC Finalizes Changes to Children's Privacy Rule](https://www.ftc.gov/news-events/news/press-releases/2025/01/ftc-finalizes-changes-childrens-privacy-rule-limiting-companies-ability-monetize-kids-data)
- [Federal Register: Children's Online Privacy Protection Rule](https://www.federalregister.gov/documents/2025/04/22/2025-05904/childrens-online-privacy-protection-rule)
- [COPPA Compliance in 2025 -- Practical Guide](https://blog.promise.legal/startup-central/coppa-compliance-in-2025-a-practical-guide-for-tech-edtech-and-kids-apps/)

### 1.2 GDPR-K (Article 8: Conditions for Child's Consent)

**Applicability**: GDPR applies to any processing of personal data of
individuals in the EU/EEA, regardless of where the processor is located.
Cena serves Israeli students but may also serve EU-resident students
(international schools, distance learners).

**Article 8 requirements**:

1. **Age threshold**: Where processing is based on consent, the child must be
   at least 16 years old to consent independently (member states may lower this
   to 13). Below this age, consent must be given or authorized by the holder of
   parental responsibility.

2. **Lawful basis for processing images**: A student uploading a photo for
   math extraction is providing the image as part of contract performance
   (GDPR Art. 6(1)(b)) -- they are using the service they signed up for.
   However, if the image incidentally contains biometric data (a face), this
   may trigger Article 9 (special categories of data) unless the processing
   is strictly limited to math extraction and the biometric data is never
   extracted or used.

3. **Data minimization** (Art. 5(1)(c)): Only data adequate, relevant, and
   limited to what is necessary should be processed. A full photograph
   containing the student's face, room, and GPS coordinates is excessive
   when only the math content is needed. This mandates technical measures
   to strip metadata, blur faces, and crop to the mathematical content
   region before further processing.

4. **Privacy by design and by default** (Art. 25): The system must implement
   appropriate technical and organizational measures at the time of design
   and during processing. Default settings must ensure only necessary data
   is processed. For image uploads, this means metadata stripping and
   content-aware cropping must be default behaviors, not opt-in features.

**CNIL guidance (France's DPA)**: The French Commission nationale de
l'informatique et des libertes has published specific recommendations for AI
system development under GDPR, emphasizing that AI systems processing images
must implement data minimization from the earliest design phase.

Sources:
- [GDPR & AI: Compliance, Challenges & Best Practices](https://www.dpo-consulting.com/blog/gdpr-and-ai-best-practices)
- [AI and Privacy: Data Protection in the Age of AI](https://gdprlocal.com/ai-and-privacy/)
- [CNIL Recommendations for AI System Development](https://www.cnil.fr/en/ai-system-development-cnils-recommendations-to-comply-gdpr)

### 1.3 Israeli Privacy Protection Law (PPL) -- Amendment 13

**Applicability**: The PPL applies to all data processing within Israel and
is the primary regulatory framework for Cena. Amendment No. 13, approved by
the Knesset on August 5, 2024, and effective mid-August 2025, represents the
most comprehensive reform to the PPL since its enactment in 1981.

**Key provisions relevant to image processing**:

1. **Information of Special Sensitivity (ISS)**: Amendment 13 defines a new
   category of "information of special sensitivity" that includes biometric
   data, health information, genetic data, and information about a person's
   family life. Facial images that can identify a student fall under this
   category when processed for identification purposes.

2. **Data minimization**: Amendment 13 explicitly requires that organizations
   avoid excessive data processing and collect only what is necessary for a
   legitimate purpose. This directly supports the ephemeral processing model:
   if the purpose is math extraction, only math content should be retained.

3. **Privacy Protection Officer (PPO)**: Organizations processing ISS or
   operating large databases must appoint a Privacy Protection Officer.
   Cena's processing of student images (even ephemerally) combined with its
   student database likely triggers this requirement.

4. **Enhanced enforcement**: The Privacy Protection Authority (PPA) now has
   authority to impose administrative orders, monetary sanctions, and
   cease-and-desist directives, with fines reaching millions of shekels.
   Multipliers apply for large-scale databases or sensitive data processing.

5. **Cross-border transfers**: The PPL's transfer provisions (Protection of
   Privacy Regulations, Transfer of Data to Databases Abroad, 5761-2001)
   apply when student images are sent to Google's Gemini API (US-based
   servers). However, under ephemeral processing with ZDR enabled, the
   image is never "stored" in a foreign database -- it is processed in
   volatile memory and discarded.

**Student-specific protections**: While Amendment 13 does not contain a
dedicated "children's data" chapter comparable to GDPR Article 8, the
combination of ISS classification for biometric data and the data
minimization requirement creates a de facto heightened protection regime for
student image data. Israeli education sector regulations (Ministry of
Education circular directives) impose additional obligations on educational
technology vendors processing student data, including periodic audits and
parental notification requirements.

Sources:
- [IAPP: Israel marks a new era in privacy law -- Amendment 13](https://iapp.org/news/a/israel-marks-a-new-era-in-privacy-law-amendment-13-ushers-in-sweeping-reform)
- [Library of Congress: Israel Amendment to Privacy Protection Law](https://www.loc.gov/item/global-legal-monitor/2025-11-17/israel-amendment-to-privacy-protection-law-goes-into-effect/)
- [Lexology: Amendment No. 13 to the Israeli Privacy Protection Law](https://www.lexology.com/library/detail.aspx?g=39750029-ad9a-41e3-a1a1-30beac789fa3)

### 1.4 FERPA (Family Educational Rights and Privacy Act)

**Applicability**: FERPA protects education records maintained by educational
agencies and institutions receiving federal funding. Cena is a vendor to
schools, making it a "school official" under the "school official exception"
(34 CFR 99.31(a)(1)) when operating under a contract with the school.

**Does a student-uploaded photo count as an education record?**

The U.S. Department of Education has provided specific guidance on this
question. Under FERPA, an education record is a record that is (1) directly
related to a student, and (2) maintained by an educational agency or
institution, or by a party acting for the agency or institution.

A photo or video is an education record when both conditions are met:

1. **Directly related**: A photo is "directly related" to a student if it is
   used for educational purposes (e.g., a student submitting a photo of their
   homework for automated grading). The Department's guidance states that
   context matters: a photo used in discipline, showing a student getting hurt,
   or showing a student violating school rules is directly related. By analogy,
   a photo submitted by a student as part of their learning activity (to get
   help with a math problem) is directly related to that student's educational
   experience.

2. **Maintained**: This is the critical distinction. FERPA only applies to
   records that are "maintained" -- i.e., stored, kept, or preserved. If Cena
   processes the photo ephemerally (in memory only, never written to disk,
   deleted within seconds), the photo is arguably not "maintained" and
   therefore not an education record under FERPA.

**However**: The extracted mathematical content (LaTeX, structured text) that
persists in the question bank IS maintained and IS directly related to the
student's educational experience. This extracted content is an education record
subject to FERPA protections. The audit log recording that a student uploaded
an image at a specific time is also an education record.

**Practical implication**: Cena's ephemeral processing model has a favorable
FERPA posture. By never persisting the image, Cena avoids creating a new
education record from the raw photo. The extracted math content inherits FERPA
protections through the existing compliance framework (StudentDataAuditMiddleware,
DataRetentionPolicy).

Sources:
- [FAQs on Photos and Videos under FERPA](https://studentprivacy.ed.gov/faq/faqs-photos-and-videos-under-ferpa)
- [When is a photo or video of a student an education record under FERPA?](https://studentprivacy.ed.gov/faq/when-photo-or-video-student-education-record-under-ferpa)

---

## 2. Data Lifecycle for Student-Uploaded Images

### 2.1 Stage 1: Upload (Client -> Server)

```
Student device                          Cena API
    |                                      |
    |  POST /api/v1/questions/from-photo   |
    |  Content-Type: multipart/form-data   |
    |  Authorization: Bearer <JWT>         |
    |  Body: image file (JPEG/PNG/WebP)    |
    |------------------------------------->|
    |                                      |
    |  TLS 1.3 (in transit encryption)     |
```

**What is transmitted**:
- The raw image file (JPEG, PNG, or WebP; max 10 MB)
- The student's authentication token (Firebase JWT)
- The tenant ID (derived from JWT claims)
- Client metadata: Content-Type, file size, request timestamp

**Encryption**: All traffic is encrypted via TLS 1.3. The image is never
transmitted in cleartext. Certificate pinning is enforced on the Flutter
mobile client to prevent MITM attacks.

**What is NOT transmitted**:
- No student name or email in the image upload request body
- No explicit student ID in the URL (derived from JWT server-side)

**Client-side preprocessing (recommended for mobile)**:
- The Flutter client SHOULD strip EXIF metadata before upload to reduce
  bandwidth and eliminate GPS coordinates at the source
- The client SHOULD compress to a maximum of 2048x2048 pixels
- The client SHOULD NOT attempt face detection (too slow on mobile)

### 2.2 Stage 2: Server-Side Preprocessing (Privacy Pipeline)

```
Raw image bytes (in memory)
    |
    v
[1. EXIF Stripping]     -- Remove ALL metadata (GPS, device, timestamps)
    |
    v
[2. Face Detection]     -- Haar cascade / DNN face detector
    |
    v
[3. Face Blurring]      -- Gaussian blur on detected face regions
    |
    v
[4. Content Cropping]   -- Detect math region, discard surrounding pixels
    |
    v
[5. SHA-256 Hashing]    -- Hash the original image for dedup/abuse logging
    |
    v
[6. Base64 Encoding]    -- Prepare for Gemini API inline_data
    |
    v
Sanitized image (in memory only)
```

**Critical invariant**: At no point in this pipeline is the image written to
disk, object storage, a temporary file, or any persistent medium. All
operations occur on `byte[]` or `Stream` objects in process memory. When
the processing pipeline completes (or fails), the byte arrays become eligible
for garbage collection and are overwritten by subsequent allocations.

### 2.3 Stage 3: Vision Model Processing (Server -> Gemini)

**What is sent to Gemini**:
- The sanitized, EXIF-stripped, face-blurred, cropped image as base64
  inline data (NOT a file URI, NOT a Google Cloud Storage reference)
- The extraction prompt (static text, no student PII)
- The model name: `gemini-2.5-flash`
- Generation config: `temperature: 0.1`, `responseMimeType: application/json`

**What is NOT sent to Gemini**:
- No student ID, name, email, or any identifying information
- No tenant/school ID
- No session context beyond the extraction prompt
- No GPS coordinates (stripped in stage 2)
- No face data (blurred in stage 2)

The existing `GeminiOcrClient.ProcessPageAsync()` in
`src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` already uses inline
base64 data rather than file URIs, which is the correct pattern for
ephemeral processing.

### 2.4 Stage 4: Result Storage (Extracted Math)

**What IS stored** (persists in PostgreSQL/Marten):
- Extracted LaTeX expressions (e.g., `f(x) = x^2 + 3x - 7`)
- Structured text with math placeholders
- Detected language (he/ar/en)
- OCR confidence score (float, 0.0--1.0)
- Question metadata: subject, topic, difficulty estimate
- Concept graph links (curriculum alignment)
- Processing audit record (see 2.5)

**What is NOT stored**:
- The original image (raw bytes)
- The sanitized image (processed bytes)
- The base64-encoded image sent to Gemini
- Any thumbnail, preview, or reduced-resolution copy
- Face detection results (bounding boxes, confidence scores)
- EXIF metadata (already stripped and discarded)

### 2.5 Stage 5: Deletion

**When is the image deleted?**

The image is not "deleted" in the traditional sense -- it is never persisted
in the first place. The lifecycle is:

| Event | Timing | State |
|-------|--------|-------|
| Image received from HTTP request | T+0 | Bytes in ASP.NET request buffer |
| EXIF stripping completes | T+50ms | New byte[] without metadata; original buffer released |
| Face detection + blur completes | T+200ms | New byte[] with blurred faces; previous buffer released |
| Content crop completes | T+300ms | Cropped byte[]; full image buffer released |
| SHA-256 hash computed | T+310ms | 64-char hex string stored; image bytes still in memory |
| Base64 encoding for Gemini | T+320ms | Base64 string created; raw bytes released |
| Gemini API call completes | T+1500ms | JSON response received; base64 string released |
| Response parsed, LaTeX extracted | T+1510ms | Only text data remains; all image data eligible for GC |
| GC collects image buffers | T+variable | Memory zeroed and reclaimed |

**Worst-case retention**: The image bytes exist in process memory for
approximately 1.5 seconds (the duration of the Gemini API call). After the
HTTP response is returned to the student, no reference to the image data
exists. The .NET garbage collector will reclaim the memory, and the image
data will be overwritten by subsequent allocations.

**Forced cleanup**: For defense in depth, the processing pipeline should
explicitly zero out byte arrays after use:

```csharp
finally
{
    if (imageBytes is not null)
        Array.Clear(imageBytes);
    if (sanitizedBytes is not null)
        Array.Clear(sanitizedBytes);
}
```

### 2.6 Stage 6: Audit Trail (What is Logged Without Storing the Image)

The audit log captures processing metadata without any image content:

| Field | Example Value | Contains PII? |
|-------|---------------|---------------|
| `ProcessingId` | `a3f7c2d1-...` (GUID) | No |
| `StudentId` | `firebase-uid-xyz` | Yes (pseudonymous) |
| `TenantId` | `school-abc` | No |
| `Timestamp` | `2026-04-12T14:30:22Z` | No |
| `ImageHashSha256` | `e3b0c44298fc...` | No (one-way hash) |
| `ImageSizeBytesOriginal` | `1482930` | No |
| `ImageSizeBytesAfterCrop` | `340221` | No |
| `ContentType` | `image/jpeg` | No |
| `ExifFieldsStripped` | `14` | No |
| `FacesDetected` | `1` | No (count only) |
| `FacesBlurred` | `1` | No (count only) |
| `CropApplied` | `true` | No |
| `GeminiModelUsed` | `gemini-2.5-flash` | No |
| `GeminiLatencyMs` | `1423` | No |
| `ExtractionConfidence` | `0.94` | No |
| `MathExpressionsFound` | `3` | No |
| `DetectedLanguage` | `he` | No |
| `ProcessingOutcome` | `success` | No |
| `ErrorCode` | `null` | No |

**Not logged**: image bytes, base64 data, EXIF content, face bounding boxes,
pixel data, thumbnails, or any data from which the original image could be
reconstructed.

The SHA-256 hash serves two purposes:
1. **Deduplication**: Detect if the same image is submitted multiple times
   (abuse detection) without storing the image itself.
2. **Audit correlation**: If a legal request requires confirming whether a
   specific image was processed, the hash can be compared against the
   provided image without Cena needing to retain the original.

---

## 3. Privacy-Preserving Architecture

### 3.1 Ephemeral Processing: Image Exists Only in Memory

The architecture follows the Zero Data Retention (ZDR) pattern described
in the serverless computing literature: inputs are processed entirely in
volatile memory, with inputs and outputs discarded the instant the result
is returned.

```
                    MEMORY BOUNDARY (no disk I/O)
                    ================================
                    |                              |
   HTTP Request --> | [EXIF] -> [Face] -> [Crop]   |
                    |      |                       |
                    |      v                       |
                    | [Gemini API Call]             |
                    |      |                       |
                    |      v                       |
                    | [Parse JSON -> LaTeX]         |
                    |      |                       |
                    ================================
                           |
                           v
                    Persistent Storage:
                    - LaTeX expressions (text only)
                    - Audit log entry (metadata only)
                    - SHA-256 hash (64 chars)
```

**Implementation strategy**: The processing pipeline runs as an in-process
service within the Cena.Actors host (or as a dedicated sidecar). It does NOT
use:
- Temporary files (`Path.GetTempFileName()`)
- Object storage (S3, GCS, Azure Blob)
- Message queues with persistent payloads (the NATS message contains only
  a processing request ID, not the image bytes)
- Database BLOB columns
- Redis or any cache with persistence

Sources:
- [Zero data retention and the case for ephemeral AI](https://www.ada.cx/blog/zero-retention-zero-risk-the-case-for-ephemeral-ai/)

### 3.2 No-Disk Policy

**Enforcement mechanisms**:

1. **Code review rule**: Any PR touching the image processing pipeline must
   be checked for `File.Write*`, `FileStream`, `Path.GetTemp*`,
   `Directory.Create*`, `BlobClient`, `PutObject`, or equivalent I/O calls.
   These are banned in the image processing namespace.

2. **Runtime guard**: The processing service runs with a restricted file
   system policy. On Linux (container deployment), the process user has
   no write permission to any directory except `/tmp` (which is a `tmpfs`
   RAM-backed filesystem, not persistent disk).

3. **CI lint rule**: A static analysis check scans for disk I/O patterns
   in the `Cena.Actors.Ingest` namespace and fails the build if found.

### 3.3 EXIF Stripping

EXIF (Exchangeable Image File Format) metadata embedded in photos can contain:

| EXIF Field | Privacy Risk | Example |
|------------|-------------|---------|
| GPS Latitude/Longitude | Reveals student's location | `32.0853, 34.7818` (Tel Aviv) |
| GPS Altitude | Location refinement | `47.3m` |
| DateTime/DateTimeOriginal | Reveals when photo was taken | `2026:04:12 14:30:22` |
| Make / Model | Device fingerprinting | `Apple iPhone 15 Pro` |
| Software | OS fingerprinting | `iOS 19.2` |
| SerialNumber | Unique device identifier | `DNQG2...` |
| LensModel | Device identification | `iPhone 15 Pro back triple camera` |
| OwnerName | Direct PII | `Yael Cohen` |
| CameraOwnerName | Direct PII | `Yael Cohen` |
| Artist | Direct PII | `Yael Cohen` |
| Copyright | Potentially PII | `(c) 2026 Yael Cohen` |
| ImageUniqueID | Tracking across submissions | `a1b2c3d4...` |
| MakerNote | Vendor-specific, may contain face/scene data | (binary blob) |
| UserComment | Free text, may contain anything | `homework q3` |

**All EXIF, IPTC, XMP, and MakerNote data must be stripped** before the image
leaves the preprocessing stage. No selective stripping -- remove everything.

The only EXIF tag that MAY be preserved is `Orientation` (tag 0x0112), which
indicates whether the image should be rotated for correct display. This tag
contains no PII (values 1--8 indicating rotation/flip). However, even this
should be applied (rotate the pixel data) and then stripped, so no metadata
survives.

Sources:
- [EXIF Data Privacy: The Ultimate Guide](https://exifdata.org/blog/exif-data-privacy-the-ultimate-guide-to-protecting-your-image-metadata)
- [Privacy Implications of EXIF Data -- EDUCAUSE](https://er.educause.edu/articles/2021/6/privacy-implications-of-exif-data)
- [EXIF Data Risks: Strip Image Metadata for Global Privacy](https://mochify.xyz/guides/exif-data-risks-image-compression-2026)

### 3.4 Face Blurring

Students may inadvertently include their face (or faces of others) in the
photo. Common scenarios:
- Taking a photo in front of a mirror
- Using the front-facing camera accidentally
- Another student visible in the background
- A teacher's face visible on a classroom poster or screen

**Detection approach**: Use a DNN-based face detector (OpenCV's
`res10_300x300_ssd_iter_140000.caffemodel` or MediaPipe Face Detection)
rather than Haar cascades. DNN detectors have significantly lower false
negative rates (missing real faces) and handle varied lighting, angles,
and partial occlusion better.

**Blurring approach**: Apply a Gaussian blur with a kernel size large enough
to render the face unrecognizable (minimum 51x51 at 300 DPI). Pixelation
(mosaic) is an alternative but Gaussian blur is harder to reverse.

**Important caveat**: Face detection adds approximately 100--200ms of latency
per image. This is acceptable within the 2-second end-to-end budget.

Sources:
- [Blur and anonymize faces with OpenCV and Python -- PyImageSearch](https://pyimagesearch.com/2020/04/06/blur-and-anonymize-faces-with-opencv-and-python/)
- [How to Blur Faces in Images using OpenCV in Python](https://thepythoncode.com/article/blur-faces-in-images-using-opencv-in-python)

### 3.5 Crop-to-Content

After EXIF stripping and face blurring, the image likely still contains
non-mathematical content: the student's desk, notebook edges, surrounding
text, room background, etc. Cropping to the mathematical content region
serves both privacy and performance goals:

- **Privacy**: Discards environmental context (room, furniture, personal items)
- **Performance**: Smaller image = fewer Gemini API input tokens = lower cost
- **Accuracy**: Focused input reduces noise for the vision model

**Detection approach**: Use contour detection (OpenCV `findContours`) to
identify the largest rectangular region containing text/math content.
Alternatively, use a simple heuristic: detect the bounding box of all
non-white (or non-background) pixels with a margin.

### 3.6 Hash-Only Logging

The SHA-256 hash of the original (pre-processing) image is computed and
stored. This hash is a one-way function: given the hash, the image cannot
be reconstructed. The hash enables:

1. **Abuse detection**: If a student uploads the same image repeatedly
   (potential automated abuse), the duplicate hash triggers rate limiting.
2. **Compliance audit**: If a regulator or parent asks "was this specific
   image processed?", the hash of the provided image can be compared.
3. **Forensic correlation**: In the event of a security incident, hashes
   can be cross-referenced without exposing image content.

The hash is stored in the audit log alongside processing metadata (see
Section 2.6). The image itself is never stored.

---

## 4. Implementation Details

### 4.1 C# Middleware for EXIF Stripping (ImageSharp)

ImageSharp (SixLabors) is the recommended library for .NET 8+ because it is
a pure managed implementation with no native dependencies, making it safe
for containerized deployment. Magick.NET is an alternative with broader
format support but requires native ImageMagick binaries.

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;

namespace Cena.Actors.Ingest.Privacy;

/// <summary>
/// Strips all EXIF/IPTC/XMP metadata from uploaded images.
/// Applies orientation correction before stripping so the image
/// renders correctly without the Orientation tag.
/// </summary>
public static class ExifStripper
{
    /// <summary>
    /// Strips all metadata from the image bytes, applying orientation
    /// correction first. Returns new byte[] with clean image data.
    /// The input array is NOT modified.
    /// </summary>
    public static async Task<ExifStripResult> StripAsync(
        byte[] imageBytes, string contentType, CancellationToken ct = default)
    {
        int fieldsStripped = 0;

        using var image = Image.Load(imageBytes);

        // Count existing metadata fields for audit logging
        if (image.Metadata.ExifProfile is { } exif)
            fieldsStripped += exif.Values.Count;
        if (image.Metadata.IptcProfile is not null)
            fieldsStripped++;
        if (image.Metadata.XmpProfile is not null)
            fieldsStripped++;

        // Apply EXIF orientation to pixel data so the image displays
        // correctly even after we remove the Orientation tag
        image.Mutate(ctx => ctx.AutoOrient());

        // Remove ALL metadata profiles
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        // Re-encode to a clean byte array
        using var output = new MemoryStream();
        if (contentType is "image/png")
        {
            await image.SaveAsPngAsync(output, ct);
        }
        else
        {
            // Default to JPEG with quality 85 (good balance of size/quality)
            await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85 }, ct);
        }

        return new ExifStripResult(
            CleanBytes: output.ToArray(),
            FieldsStripped: fieldsStripped,
            OriginalWidth: image.Width,
            OriginalHeight: image.Height);
    }
}

public sealed record ExifStripResult(
    byte[] CleanBytes,
    int FieldsStripped,
    int OriginalWidth,
    int OriginalHeight);
```

**Alternative with Magick.NET** (if broader format support is needed):

```csharp
using ImageMagick;

public static byte[] StripWithMagickNet(byte[] imageBytes)
{
    using var image = new MagickImage(imageBytes);

    // Auto-orient based on EXIF, then strip all profiles
    image.AutoOrient();
    image.RemoveProfile("exif");
    image.RemoveProfile("iptc");
    image.RemoveProfile("xmp");
    image.RemoveProfile("8bim"); // Photoshop metadata

    // Strip all remaining metadata
    image.Strip();

    return image.ToByteArray(MagickFormat.Jpeg);
}
```

Sources:
- [How to remove metadata from images in .NET](https://lioncoding.com/how-to-remove-metadata-from-images-in-.net/)
- [ImageSharp Issue #400: Remove EXIF from image](https://github.com/SixLabors/ImageSharp/issues/400)
- [Remove GPS location from images -- C# Snipplr](https://snipplr.com/view/147163/remove-gps-location-from-images-or-photos-exif-metadata-removal)

### 4.2 Python Preprocessor for Face Detection + Blurring (OpenCV)

If the face detection/blurring step is implemented as a Python sidecar
(recommended for access to the best-in-class OpenCV DNN models), the
following service can be called from the .NET pipeline via HTTP or gRPC:

```python
"""
Privacy preprocessor sidecar for student image uploads.
Detects and blurs faces, crops to math content region.
Runs as a stateless HTTP service -- no disk I/O, no logging of image data.
"""
import cv2
import numpy as np
from io import BytesIO
from fastapi import FastAPI, UploadFile, Response
from fastapi.responses import StreamingResponse
import hashlib

app = FastAPI()

# Load DNN face detector (more accurate than Haar cascades)
PROTO_PATH = "deploy.prototxt"
MODEL_PATH = "res10_300x300_ssd_iter_140000.caffemodel"
face_net = cv2.dnn.readNetFromCaffe(PROTO_PATH, MODEL_PATH)

FACE_CONFIDENCE_THRESHOLD = 0.5
BLUR_KERNEL_SIZE = (51, 51)  # Must be odd; larger = more blur


def detect_and_blur_faces(image: np.ndarray) -> tuple[np.ndarray, int]:
    """
    Detect faces using OpenCV DNN and apply Gaussian blur.
    Returns the blurred image and count of faces detected.
    """
    (h, w) = image.shape[:2]
    blob = cv2.dnn.blobFromImage(
        cv2.resize(image, (300, 300)),
        scalefactor=1.0,
        size=(300, 300),
        mean=(104.0, 177.0, 123.0),
    )
    face_net.setInput(blob)
    detections = face_net.forward()

    faces_found = 0
    for i in range(detections.shape[2]):
        confidence = detections[0, 0, i, 2]
        if confidence < FACE_CONFIDENCE_THRESHOLD:
            continue

        box = detections[0, 0, i, 3:7] * np.array([w, h, w, h])
        (x1, y1, x2, y2) = box.astype("int")

        # Clamp to image bounds
        x1, y1 = max(0, x1), max(0, y1)
        x2, y2 = min(w, x2), min(h, y2)

        if x2 <= x1 or y2 <= y1:
            continue

        # Apply Gaussian blur to the face region
        face_roi = image[y1:y2, x1:x2]
        blurred_face = cv2.GaussianBlur(face_roi, BLUR_KERNEL_SIZE, 30)
        image[y1:y2, x1:x2] = blurred_face
        faces_found += 1

    return image, faces_found


def crop_to_content(image: np.ndarray, margin: int = 20) -> np.ndarray:
    """
    Crop image to the bounding box of non-white content.
    Adds a margin around the detected content region.
    """
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

    # Threshold: assume background is white or near-white
    _, binary = cv2.threshold(gray, 240, 255, cv2.THRESH_BINARY_INV)

    # Find contours of content regions
    contours, _ = cv2.findContours(
        binary, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE
    )

    if not contours:
        return image  # No content detected; return original

    # Get bounding box of all contours combined
    all_points = np.vstack(contours)
    x, y, w, h = cv2.boundingRect(all_points)

    # Add margin, clamped to image bounds
    (ih, iw) = image.shape[:2]
    x1 = max(0, x - margin)
    y1 = max(0, y - margin)
    x2 = min(iw, x + w + margin)
    y2 = min(ih, y + h + margin)

    return image[y1:y2, x1:x2]


@app.post("/preprocess")
async def preprocess_image(file: UploadFile) -> Response:
    """
    Privacy preprocessing endpoint.
    Input: raw image (JPEG/PNG)
    Output: face-blurred, content-cropped image (JPEG)
    No disk I/O. No image data logged.
    """
    contents = await file.read()
    nparr = np.frombuffer(contents, np.uint8)
    image = cv2.imdecode(nparr, cv2.IMREAD_COLOR)

    if image is None:
        return Response(status_code=400, content="Invalid image")

    # Step 1: Detect and blur faces
    image, faces_count = detect_and_blur_faces(image)

    # Step 2: Crop to math content region
    cropped = crop_to_content(image)

    # Step 3: Encode as JPEG (no metadata)
    _, buffer = cv2.imencode(".jpg", cropped, [cv2.IMWRITE_JPEG_QUALITY, 85])

    # Explicitly clear the original image array
    contents = b""
    nparr = np.zeros(1, dtype=np.uint8)

    return Response(
        content=buffer.tobytes(),
        media_type="image/jpeg",
        headers={
            "X-Faces-Detected": str(faces_count),
            "X-Cropped": "true",
        },
    )
```

**Deployment**: This sidecar runs as a container in the same pod/task as the
Cena.Actors host. Communication is over localhost (no network traversal for
image data). The container has no persistent volume mounts.

### 4.3 Ephemeral Processing Pipeline Architecture

The complete pipeline orchestrated from the .NET host:

```csharp
using System.Security.Cryptography;

namespace Cena.Actors.Ingest.Privacy;

/// <summary>
/// Orchestrates the privacy-preserving image processing pipeline.
/// All image data stays in memory. No disk I/O. No persistent storage.
/// </summary>
public sealed class PrivacyImagePipeline
{
    private readonly IFaceBlurService _faceBlur;
    private readonly GeminiOcrClient _gemini;
    private readonly IImageProcessingAuditLogger _auditLogger;

    public PrivacyImagePipeline(
        IFaceBlurService faceBlur,
        GeminiOcrClient gemini,
        IImageProcessingAuditLogger auditLogger)
    {
        _faceBlur = faceBlur;
        _gemini = gemini;
        _auditLogger = auditLogger;
    }

    public async Task<ImageProcessingResult> ProcessAsync(
        byte[] rawImageBytes,
        string contentType,
        string studentId,
        string tenantId,
        CancellationToken ct = default)
    {
        var processingId = Guid.NewGuid();
        var startTime = DateTimeOffset.UtcNow;
        byte[]? sanitizedBytes = null;

        try
        {
            // 1. Compute hash of original image (for dedup/audit)
            var imageHash = ComputeSha256(rawImageBytes);

            // 2. Strip EXIF metadata
            var stripResult = await ExifStripper.StripAsync(
                rawImageBytes, contentType, ct);
            var cleanBytes = stripResult.CleanBytes;

            // 3. Face detection + blurring (via Python sidecar)
            var blurResult = await _faceBlur.BlurFacesAsync(
                cleanBytes, ct);
            sanitizedBytes = blurResult.ProcessedBytes;
            var facesDetected = blurResult.FacesDetected;
            var facesBlurred = blurResult.FacesBlurred;

            // Original clean bytes no longer needed
            Array.Clear(cleanBytes);

            // 4. Send sanitized image to Gemini for math extraction
            using var stream = new MemoryStream(sanitizedBytes);
            var ocrResult = await _gemini.ProcessPageAsync(
                stream, "image/jpeg", ct);

            // 5. Log audit trail (metadata only, no image data)
            await _auditLogger.LogAsync(new ImageProcessingAuditEntry
            {
                ProcessingId = processingId,
                StudentId = studentId,
                TenantId = tenantId,
                Timestamp = startTime,
                ImageHashSha256 = imageHash,
                ImageSizeBytesOriginal = rawImageBytes.Length,
                ImageSizeBytesAfterCrop = sanitizedBytes.Length,
                ContentType = contentType,
                ExifFieldsStripped = stripResult.FieldsStripped,
                FacesDetected = facesDetected,
                FacesBlurred = facesBlurred,
                CropApplied = true,
                GeminiModelUsed = _gemini.ProviderName,
                GeminiLatencyMs = (int)(DateTimeOffset.UtcNow - startTime)
                    .TotalMilliseconds,
                ExtractionConfidence = ocrResult.Confidence,
                MathExpressionsFound = ocrResult.MathExpressions.Count,
                DetectedLanguage = ocrResult.DetectedLanguage,
                ProcessingOutcome = "success"
            });

            return new ImageProcessingResult(
                ProcessingId: processingId,
                OcrOutput: ocrResult,
                ImageHash: imageHash,
                Success: true,
                Error: null);
        }
        catch (Exception ex)
        {
            await _auditLogger.LogAsync(new ImageProcessingAuditEntry
            {
                ProcessingId = processingId,
                StudentId = studentId,
                TenantId = tenantId,
                Timestamp = startTime,
                ProcessingOutcome = "failure",
                ErrorCode = ex.GetType().Name
            });

            return new ImageProcessingResult(
                ProcessingId: processingId,
                OcrOutput: null,
                ImageHash: null,
                Success: false,
                Error: ex.Message);
        }
        finally
        {
            // CRITICAL: Zero out all image byte arrays
            if (rawImageBytes is not null)
                Array.Clear(rawImageBytes);
            if (sanitizedBytes is not null)
                Array.Clear(sanitizedBytes);
        }
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }
}

public sealed record ImageProcessingResult(
    Guid ProcessingId,
    OcrPageOutput? OcrOutput,
    string? ImageHash,
    bool Success,
    string? Error);

public sealed record ImageProcessingAuditEntry
{
    public Guid ProcessingId { get; init; }
    public string StudentId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public string? ImageHashSha256 { get; init; }
    public int ImageSizeBytesOriginal { get; init; }
    public int ImageSizeBytesAfterCrop { get; init; }
    public string ContentType { get; init; } = "";
    public int ExifFieldsStripped { get; init; }
    public int FacesDetected { get; init; }
    public int FacesBlurred { get; init; }
    public bool CropApplied { get; init; }
    public string GeminiModelUsed { get; init; } = "";
    public int GeminiLatencyMs { get; init; }
    public float ExtractionConfidence { get; init; }
    public int MathExpressionsFound { get; init; }
    public string DetectedLanguage { get; init; } = "";
    public string ProcessingOutcome { get; init; } = "";
    public string? ErrorCode { get; init; }
}
```

### 4.4 Audit Log Schema

The `ImageProcessingAuditEntry` shown above is stored as a Marten document
in PostgreSQL. It integrates with the existing compliance infrastructure:

```sql
-- Marten auto-creates this table; schema shown for reference
CREATE TABLE IF NOT EXISTS mt_doc_imageprocessingauditentry (
    id              uuid PRIMARY KEY,
    data            jsonb NOT NULL,
    mt_version      integer NOT NULL DEFAULT 0,
    mt_dotnet_type  varchar NOT NULL
);

-- Indexes for compliance queries
CREATE INDEX ix_img_audit_student
    ON mt_doc_imageprocessingauditentry ((data->>'StudentId'));
CREATE INDEX ix_img_audit_tenant
    ON mt_doc_imageprocessingauditentry ((data->>'TenantId'));
CREATE INDEX ix_img_audit_timestamp
    ON mt_doc_imageprocessingauditentry ((data->>'Timestamp'));
CREATE INDEX ix_img_audit_hash
    ON mt_doc_imageprocessingauditentry ((data->>'ImageHashSha256'));
```

**What is logged** (for compliance audit):
- Processing ID, student ID (pseudonymous), tenant ID
- Timestamp, image hash, file sizes
- Count of EXIF fields stripped, faces detected/blurred
- Gemini model used, latency, confidence, language
- Processing outcome (success/failure) and error code

**What is NOT logged**:
- Image bytes (raw or processed)
- EXIF field values (only the count)
- Face bounding box coordinates
- Base64 image data
- Gemini prompt or response content
- Any data from which the image could be reconstructed

### 4.5 Data Retention Schedule for Image Processing Artifacts

| Artifact | Retention | Basis |
|----------|-----------|-------|
| Raw image bytes | 0 seconds (never persisted) | COPPA 312.10, GDPR Art. 5(1)(e) |
| Sanitized image bytes | 0 seconds (never persisted) | Same |
| Base64 Gemini payload | 0 seconds (in-memory only) | Same |
| EXIF metadata values | 0 seconds (stripped and discarded) | GDPR Art. 5(1)(c) |
| Face detection results | 0 seconds (count logged, data discarded) | GDPR Art. 5(1)(c) |
| Image SHA-256 hash | 2 years (analytics retention) | Dedup/abuse detection |
| Processing audit log | 5 years (audit log retention) | FERPA, Israeli PPL |
| Extracted LaTeX/text | 7 years (student record) | FERPA 34 CFR 99 |

These periods align with the existing `DataRetentionPolicy` constants in
`src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs`.

---

## 5. Gemini API Specifics

### 5.1 Does Google Retain Uploaded Images?

**Paid API (Gemini Developer API with billing enabled)**:

When using the paid tier of the Gemini API, Google states:
- Prompts and responses are NOT used to improve Google's models.
- Processing is governed by Google's Data Processing Addendum (DPA).
- Google logs prompts and responses for a limited period solely for abuse
  monitoring (detecting violations of the Prohibited Use Policy).

**Zero Data Retention (ZDR)**:

Google offers a Zero Data Retention mode for the Gemini API. When ZDR is
enabled for a project:
- All user content (prompts and responses, including inline image data)
  and identifiable metadata are cleared prior to any abuse monitoring
  logging.
- The resulting log record is marked as "sanitized" and contains zero
  identifiable user data.
- Data is strictly in RAM (not at-rest), isolated at the project level,
  with a 24-hour TTL for the sanitized metadata.

**How to enable ZDR**:
1. The project must be on the paid tier (not free tier).
2. Submit a ZDR opt-out form to Google, requesting that abuse monitoring
   logging be disabled for the project, OR
3. Set up invoiced Cloud Billing, which automatically qualifies for ZDR.

**Important exception**: If using "Grounding with Google Search," Google
retains prompts and outputs for 30 days. Cena does NOT use grounding with
Google Search for the OCR pipeline, so this exception does not apply.

Sources:
- [Zero data retention in the Gemini Developer API](https://ai.google.dev/gemini-api/docs/zdr)
- [Vertex AI and zero data retention](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/vertex-ai-zero-data-retention)
- [Gemini API Additional Terms of Service](https://ai.google.dev/gemini-api/terms)
- [Gemini API & Data Privacy: What Google's AI Terms Mean for You in 2025](https://redact.dev/blog/gemini-api-terms-2025)

### 5.2 How to Use the API in "No Storage" Mode

The existing `GeminiOcrClient` in Cena already uses the correct pattern:

1. **Inline data** (`inline_data` with base64): The image is sent as part of
   the request body, not uploaded to Google Cloud Storage. This means no
   persistent copy exists in a GCS bucket.

2. **No caching**: The request does not use Gemini's context caching feature,
   which would store data for reuse across requests.

3. **No tuning**: The image is not used for model fine-tuning.

Additional configuration required:

```csharp
// Ensure the Gemini request does NOT enable any persistence features
var request = new GeminiRequest
{
    Contents = new[] { /* ... inline_data ... */ },
    GenerationConfig = new GeminiGenerationConfig
    {
        Temperature = 0.1f,
        ResponseMimeType = "application/json"
    }
    // Do NOT set: cachedContent, tools[].googleSearchRetrieval
};
```

### 5.3 Alternatives if Gemini Retains Data

If Google's ZDR guarantees are insufficient for a specific regulatory
environment, alternatives include:

| Alternative | Pros | Cons |
|-------------|------|------|
| **Self-hosted vision model** (e.g., LLaVA, Florence-2) | Full data control, no third-party transfer | Lower accuracy for math OCR, higher infra cost, GPU required |
| **Mathpix** (existing fallback in codebase) | Purpose-built for math OCR, high accuracy | Third-party processor, US-based, requires DPA |
| **Azure AI Vision with Customer Managed Keys** | Enterprise DPA, EU region options | Different API, migration cost |
| **On-premise Gemini via Vertex AI** (GKE with VPC-SC) | Google model quality, data stays in controlled environment | Requires Google Cloud infrastructure, complex setup |

The current architecture (Gemini primary, Mathpix fallback) is sound provided
ZDR is enabled on the Gemini project. The `IOcrClient` abstraction in
`src/actors/Cena.Actors/Ingest/IOcrClient.cs` allows swapping providers
without changing the pipeline.

---

## 6. Cost Impact of Privacy Measures

### 6.1 Latency Impact

| Privacy Step | Added Latency | Notes |
|--------------|---------------|-------|
| EXIF stripping (ImageSharp) | ~30--50ms | In-process, CPU-bound |
| Face detection (OpenCV DNN) | ~100--200ms | Sidecar call, GPU optional |
| Face blurring (Gaussian) | ~5--10ms | Per face, negligible |
| Content cropping | ~20--40ms | Contour detection + crop |
| SHA-256 hashing | ~5--10ms | For a 5MB image |
| **Total privacy overhead** | **~160--310ms** | |

**Budget analysis**: The AUTORESEARCH_CONFIG specifies a 2-second end-to-end
latency target. The Gemini API call takes ~1000--1500ms. Adding 160--310ms
of privacy preprocessing keeps the total at ~1160--1810ms, within budget.

### 6.2 Compute Cost Impact

| Component | Cost Per 1K Images | Monthly (100K images) |
|-----------|--------------------|-----------------------|
| Gemini 2.5 Flash (vision) | ~$0.30 | ~$30 |
| Face detection sidecar (CPU) | ~$0.02 | ~$2 |
| ImageSharp processing (CPU) | ~$0.01 | ~$1 |
| **Total privacy overhead** | **~$0.03/1K** | **~$3/month** |

The privacy preprocessing adds approximately 10% to the per-image processing
cost, which is negligible relative to the Gemini API cost. The
AUTORESEARCH_CONFIG estimates ~$200/month at 100K photos for the full
pipeline; privacy measures add ~$3/month.

### 6.3 Bandwidth Savings

Content cropping reduces image size by 40--70% on average (full notebook page
to cropped math region). This reduces:
- Upload bandwidth (student -> server): not affected (cropping is server-side)
- Gemini API payload: 40--70% smaller base64 string
- Gemini API cost: proportionally lower (fewer input tokens)

Net effect: **content cropping saves more than it costs**, making the privacy
pipeline cost-neutral or cost-positive.

---

## 7. Compliance Checklist (20 Items)

### Legal and Policy

| # | Item | Status | Reference |
|---|------|--------|-----------|
| 1 | Written data retention policy for image data specifying zero-persistence | PLANNED | COPPA 312.10 (amended) |
| 2 | Privacy notice updated to disclose image processing and Gemini API use | PLANNED | COPPA 312.4, GDPR Art. 13 |
| 3 | Parental consent flow covers image upload feature for under-16s | PLANNED | GDPR Art. 8, COPPA 312.5 |
| 4 | Data Processing Agreement (DPA) with Google for Gemini API | PLANNED | GDPR Art. 28, Israeli PPL |
| 5 | DPIA updated to include image processing pipeline risks | PLANNED | GDPR Art. 35 |
| 6 | FERPA school official agreement covers image processing | PLANNED | FERPA 34 CFR 99.31(a)(1) |

### Technical -- Preprocessing

| # | Item | Status | Reference |
|---|------|--------|-----------|
| 7 | EXIF/IPTC/XMP metadata stripped from all uploaded images | PLANNED | GDPR Art. 5(1)(c), 25 |
| 8 | Orientation applied to pixels before metadata removal | PLANNED | Functional requirement |
| 9 | Face detection applied to all images before Gemini transmission | PLANNED | GDPR Art. 25, COPPA biometrics |
| 10 | Detected faces blurred with Gaussian kernel >= 51x51 | PLANNED | Privacy by design |
| 11 | Content-aware cropping applied to reduce non-math pixels | PLANNED | GDPR Art. 5(1)(c) |
| 12 | SHA-256 hash computed and logged for dedup/audit | PLANNED | Audit requirement |

### Technical -- Storage and Memory

| # | Item | Status | Reference |
|---|------|--------|-----------|
| 13 | Images never written to disk (no File.Write, no temp files) | PLANNED | Zero-persistence policy |
| 14 | Image byte arrays zeroed after processing (Array.Clear) | PLANNED | Defense in depth |
| 15 | No image data in application logs (structured logging verified) | PLANNED | GDPR Art. 5(1)(c) |
| 16 | No image data in error/exception messages or stack traces | PLANNED | Operational security |

### Technical -- Third-Party API

| # | Item | Status | Reference |
|---|------|--------|-----------|
| 17 | Gemini API project on paid tier with ZDR enabled | PLANNED | Google ZDR policy |
| 18 | Gemini requests use inline_data (not GCS URIs) | EXISTING | GeminiOcrClient.cs |
| 19 | Gemini requests do not use context caching or grounding | EXISTING | GeminiOcrClient.cs |
| 20 | No student PII included in Gemini prompts | EXISTING | ExtractionPrompt is static |

### Audit and Monitoring

| # | Item | Status | Reference |
|---|------|--------|-----------|
| 21 | Image processing audit log captures metadata only | PLANNED | Section 2.6 |
| 22 | CI lint rule prevents disk I/O in image processing namespace | PLANNED | Section 3.2 |
| 23 | StudentDataAuditMiddleware covers image upload endpoint | PLANNED | FERPA REV-013 |
| 24 | Right-to-erasure covers image processing audit logs | PLANNED | GDPR Art. 17 |

---

## 8. Security Score Contribution

The privacy-preserving image processing pipeline contributes to the
cumulative Security Robustness Score as follows:

| Defense Layer | Points | Rationale |
|---------------|--------|-----------|
| EXIF metadata stripping | 3 | Eliminates GPS, device ID, owner name leakage |
| Face detection and blurring | 3 | Prevents biometric data transmission to Gemini |
| Content-aware cropping | 2 | Reduces environmental/contextual PII exposure |
| Zero-persistence memory-only pipeline | 3 | No image data at rest to breach |
| SHA-256 hash-only logging | 1 | Audit capability without image retention |
| Gemini ZDR configuration | 1 | Ensures third-party processor also retains nothing |
| Explicit byte array zeroing | 1 | Defense in depth against memory forensics |
| **Total iteration 6 contribution** | **14** | |

**Cumulative score after iteration 6**: Target 72/100 (assuming iterations
1--5 contributed 58 points).

---

## 9. Cena Codebase Integration Points

The following existing files are directly relevant to this privacy pipeline:

| File | Relevance |
|------|-----------|
| `src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` | Current Gemini integration; already uses inline_data (correct pattern). Privacy pipeline wraps this. |
| `src/actors/Cena.Actors/Ingest/IOcrClient.cs` | Abstraction layer; privacy pipeline sits upstream of any IOcrClient implementation. |
| `src/actors/Cena.Actors/Ingest/ContentExtractorService.cs` | Downstream consumer of OCR output; receives only text/LaTeX, never image bytes. |
| `src/shared/Cena.Infrastructure/Compliance/ProcessingPurpose.cs` | Needs new enum value `ImageProcessing` with `[LawfulBasis(Contract)]` and `[MinorDefault(true)]`. |
| `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` | Needs new constant `ImageProcessingAuditRetention = TimeSpan.FromDays(365 * 5)`. |
| `src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs` | AuditedPaths set needs `/api/v1/questions/from-photo` added. |
| `src/shared/Cena.Infrastructure/Compliance/StudentRecordAccessLog.cs` | Audit log model; ImageProcessingAuditEntry is a separate document for image-specific metadata. |
| `docs/legal/privacy-policy.md` | Must be updated to disclose image processing and Gemini API use for photo-to-math extraction. |
| `docs/compliance/dpia-2026-04.md` | Must be updated with image processing risk assessment (new risk R-13). |

---

## 10. Open Questions and Recommendations

1. **Client-side EXIF stripping**: Should the Flutter/Vue client strip EXIF
   before upload? This reduces bandwidth and provides defense-in-depth, but
   server-side stripping must still occur (client could be bypassed).
   **Recommendation**: Do both. Client-side for bandwidth savings, server-side
   for security guarantee.

2. **Face detection false negatives**: No face detector is 100% accurate.
   If a face is missed, it will be sent to Gemini. The risk is mitigated by
   ZDR (Gemini does not retain the image), but this is a residual risk.
   **Recommendation**: Accept the residual risk; document in the DPIA.

3. **Gemini ZDR approval timeline**: Google's ZDR form takes "up to 2 weeks"
   for approval. This must be initiated before the screenshot analyzer
   feature goes to production.
   **Recommendation**: Submit the ZDR form immediately; track as a
   pre-launch blocker.

4. **Python sidecar vs. .NET-native face detection**: Running a Python
   sidecar adds operational complexity. .NET-native options exist
   (OpenCvSharp, Emgu CV) but have weaker model ecosystems.
   **Recommendation**: Start with the Python sidecar; evaluate .NET-native
   options if the sidecar proves operationally burdensome.

5. **Israeli Ministry of Education notification**: Does deploying an AI
   image processing feature in an educational platform require notification
   to the Israeli Ministry of Education? Amendment 13 does not explicitly
   address this, but sector-specific circular directives may apply.
   **Recommendation**: Consult with Israeli education law counsel before
   production deployment.

---

## References

### Legal and Regulatory
- [FTC: COPPA Rule Amendments (Jan 2025)](https://www.ftc.gov/news-events/news/press-releases/2025/01/ftc-finalizes-changes-childrens-privacy-rule-limiting-companies-ability-monetize-kids-data)
- [Federal Register: COPPA Rule (Apr 2025)](https://www.federalregister.gov/documents/2025/04/22/2025-05904/childrens-online-privacy-protection-rule)
- [COPPA Compliance in 2025: Practical Guide](https://blog.promise.legal/startup-central/coppa-compliance-in-2025-a-practical-guide-for-tech-edtech-and-kids-apps/)
- [Children's Online Privacy in 2025: Amended COPPA Rule](https://www.loeb.com/en/insights/publications/2025/05/childrens-online-privacy-in-2025-the-amended-coppa-rule)
- [FERPA: FAQs on Photos and Videos](https://studentprivacy.ed.gov/faq/faqs-photos-and-videos-under-ferpa)
- [FERPA: When is a photo an education record?](https://studentprivacy.ed.gov/faq/when-photo-or-video-student-education-record-under-ferpa)
- [IAPP: Israel Amendment 13](https://iapp.org/news/a/israel-marks-a-new-era-in-privacy-law-amendment-13-ushers-in-sweeping-reform)
- [Lexology: Israeli PPL Amendment 13](https://www.lexology.com/library/detail.aspx?g=39750029-ad9a-41e3-a1a1-30beac789fa3)
- [Library of Congress: Israel PPL Amendment](https://www.loc.gov/item/global-legal-monitor/2025-11-17/israel-amendment-to-privacy-protection-law-goes-into-effect/)
- [GDPR & AI Best Practices](https://www.dpo-consulting.com/blog/gdpr-and-ai-best-practices)
- [CNIL: AI System Development Recommendations](https://www.cnil.fr/en/ai-system-development-cnils-recommendations-to-comply-gdpr)

### Technical
- [Google: Gemini API Zero Data Retention](https://ai.google.dev/gemini-api/docs/zdr)
- [Google: Vertex AI ZDR](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/vertex-ai-zero-data-retention)
- [Google: Gemini API Terms of Service](https://ai.google.dev/gemini-api/terms)
- [Gemini API & Data Privacy 2025](https://redact.dev/blog/gemini-api-terms-2025)
- [EXIF Data Privacy Guide](https://exifdata.org/blog/exif-data-privacy-the-ultimate-guide-to-protecting-your-image-metadata)
- [EDUCAUSE: Privacy Implications of EXIF Data](https://er.educause.edu/articles/2021/6/privacy-implications-of-exif-data)
- [EXIF Data Risks 2026](https://mochify.xyz/guides/exif-data-risks-image-compression-2026)
- [Removing metadata from images in .NET](https://lioncoding.com/how-to-remove-metadata-from-images-in-.net/)
- [ImageSharp EXIF Removal](https://github.com/SixLabors/ImageSharp/issues/400)
- [PyImageSearch: Face Blurring with OpenCV](https://pyimagesearch.com/2020/04/06/blur-and-anonymize-faces-with-opencv-and-python/)
- [Zero Data Retention and Ephemeral AI](https://www.ada.cx/blog/zero-retention-zero-risk-the-case-for-ephemeral-ai/)

### Cena Platform (Internal)
- `docs/legal/privacy-policy.md` -- Platform privacy policy (DRAFT)
- `docs/compliance/dpia-2026-04.md` -- Data Protection Impact Assessment (DRAFT)
- `src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` -- Existing Gemini integration
- `src/shared/Cena.Infrastructure/Compliance/ProcessingPurpose.cs` -- GDPR processing purposes
- `src/shared/Cena.Infrastructure/Compliance/DataRetentionPolicy.cs` -- Retention periods
- `src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs` -- FERPA audit middleware
