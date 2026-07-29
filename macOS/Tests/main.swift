import Foundation

// A command-line harness over the non-UI half of TrackForge. Run with ./Tests/run.sh
//
// The tag writer and the name formatter are the two places a bug silently
// corrupts a music library rather than showing an error, so they get the most
// coverage here.

var failures = 0
var checks = 0

func check(_ label: String, _ condition: @autoclosure () -> Bool, _ detail: String = "") {
    checks += 1
    if condition() {
        print("  ok   \(label)")
    } else {
        failures += 1
        print("  FAIL \(label)\(detail.isEmpty ? "" : "  — \(detail)")")
    }
}

func equal<T: Equatable>(_ label: String, _ actual: T, _ expected: T) {
    check(label, actual == expected, "got \(actual), wanted \(expected)")
}

func section(_ name: String) { print("\n\(name)") }

// MARK: - Fixtures

let scratch = URL(fileURLWithPath: NSTemporaryDirectory())
    .appendingPathComponent("trackforge-selftest-\(UUID().uuidString)")
try! FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: scratch) }

/// A stand-in MP3. The ID3 layer never looks at the audio, so a recognisable
/// byte pattern is enough to prove the payload survives a tag rewrite intact.
func makeFile(named name: String, audio: Data, existingTag: Data = Data()) -> String {
    let url = scratch.appendingPathComponent(name)
    var contents = existingTag
    contents.append(audio)
    try! contents.write(to: url)
    return url.path
}

let audioMarker = Data((0..<4096).map { UInt8($0 % 251) })

// MARK: - NameFormatter

section("NameFormatter.titleCase")
// The lowercase-word set is deliberately small — "of" and "on" are NOT in it,
// because the library this convention came from capitalises them.
equal("of stays capitalised", NameFormatter.titleCase("vicinity of obscenity"),
      "Vicinity Of Obscenity")
equal("a lowercased mid-title", NameFormatter.titleCase("system of a down"),
      "System Of a Down")
equal("the lowercased mid-title", NameFormatter.titleCase("bat out of the hell"),
      "Bat Out Of the Hell")
equal("leading connector kept", NameFormatter.titleCase("the trooper"), "The Trooper")
equal("trailing connector kept", NameFormatter.titleCase("what is it and"), "What Is It And")
equal("acronym untouched", NameFormatter.titleCase("B.Y.O.B."), "B.Y.O.B.")
equal("inner caps untouched", NameFormatter.titleCase("iTunes rocks"), "iTunes Rocks")
equal("bracket then letter", NameFormatter.titleCase("(deluxe edition)"), "(Deluxe Edition)")
equal("underscores become spaces", NameFormatter.titleCase("hello_world"), "Hello World")

section("NameFormatter.buildFileName")
let sample = Track()
sample.title = "vicinity of obscenity"
sample.artist = "system of a down"
sample.album = "steal this album!"
sample.year = "2002"
sample.trackNumber = "9"

equal("default pattern",
      NameFormatter.buildFileName(sample, pattern: "{track} {title}", extension: ".mp3"),
      "09 Vicinity Of Obscenity.mp3")
equal("artist pattern",
      NameFormatter.buildFileName(sample, pattern: "{artist} - {title}", extension: "mp3"),
      "System Of a Down - Vicinity Of Obscenity.mp3")
equal("illegal characters stripped",
      NameFormatter.safeFileName("A/B:C*D?"), "ABCD")
equal("empty falls back", NameFormatter.safeFileName("   "), "Untitled")

let missingTrack = Track()
missingTrack.title = "no number"
equal("absent token collapses",
      NameFormatter.buildFileName(missingTrack, pattern: "{track} {title}", extension: ".mp3"),
      "No Number.mp3")

// MARK: - ID3 round trip

section("ID3 write → read round trip")

var tag = ID3.Tag()
tag.title = "Vicinity of Obscenity"
tag.artists = ["System of a Down"]
tag.albumArtists = ["System of a Down"]
tag.album = "Steal This Album!"
tag.genre = "Alternative Metal"
tag.year = "2002"
tag.trackNumber = "9"
tag.trackCount = "16"
tag.discNumber = "1"
tag.bpm = "142"
tag.musicalKey = "F#m"
tag.camelot = "11A"
tag.isrc = "USSM10212345"
tag.publisher = "American Recordings"
tag.composer = "Daron Malakian"
tag.comment = "Ripped by TrackForge"
tag.sourceURL = "https://www.youtube.com/watch?v=abcdefghijk"
tag.rating = 4

let cover = Data((0..<2048).map { UInt8(($0 * 7) % 256) })
tag.pictures = [ID3.Picture(mimeType: "image/jpeg", pictureType: 3,
                            description: "Cover", data: cover)]

let path = makeFile(named: "roundtrip.mp3", audio: audioMarker)
try! ID3.write(tag, to: path)

let read = ID3.read(path: path)!
equal("title", read.title, tag.title)
equal("artist", read.artists, tag.artists)
equal("album artist", read.albumArtists, tag.albumArtists)
equal("album", read.album, tag.album)
equal("genre", read.genre, tag.genre)
equal("year", read.year, tag.year)
equal("track number", read.trackNumber, tag.trackNumber)
equal("track count", read.trackCount, tag.trackCount)
equal("disc number", read.discNumber, tag.discNumber)
equal("bpm", read.bpm, tag.bpm)
equal("musical key", read.musicalKey, tag.musicalKey)
equal("camelot (TXXX)", read.camelot, tag.camelot)
equal("isrc", read.isrc, tag.isrc)
equal("publisher", read.publisher, tag.publisher)
equal("composer", read.composer, tag.composer)
equal("comment", read.comment, tag.comment)
equal("source url (WOAS)", read.sourceURL, tag.sourceURL)
equal("rating", read.rating, tag.rating)
equal("picture count", read.pictures.count, 1)
equal("picture bytes", read.pictures.first?.data, cover)
equal("picture mime", read.pictures.first?.mimeType, "image/jpeg")

section("ID3 file integrity")
let writtenBytes = try! Data(contentsOf: URL(fileURLWithPath: path))
check("starts with an ID3 header",
      writtenBytes.prefix(3) == Data([0x49, 0x44, 0x33]))
equal("declares version 2.3", writtenBytes[3], 3)
check("audio payload survives byte for byte",
      writtenBytes.suffix(audioMarker.count) == audioMarker)

section("ID3 unicode and encoding")
var unicode = ID3.Tag()
unicode.title = "Björk — Jóga ♪"
unicode.artists = ["Sigur Rós"]
unicode.album = "Ágætis byrjun"
let unicodePath = makeFile(named: "unicode.mp3", audio: audioMarker)
try! ID3.write(unicode, to: unicodePath)
let unicodeRead = ID3.read(path: unicodePath)!
equal("utf-16 title survives", unicodeRead.title, unicode.title)
equal("utf-16 artist survives", unicodeRead.artists, unicode.artists)
equal("utf-16 album survives", unicodeRead.album, unicode.album)

section("ID3 strips a legacy v1 trailer")
var v1 = Data("TAG".utf8)
v1.append(Data(repeating: 0x20, count: 125))
let legacyPath = makeFile(named: "legacy.mp3", audio: audioMarker + v1)
var simple = ID3.Tag()
simple.title = "Kept"
try! ID3.write(simple, to: legacyPath)
let legacyBytes = try! Data(contentsOf: URL(fileURLWithPath: legacyPath))
check("no ID3v1 trailer remains",
      legacyBytes.suffix(128).prefix(3) != Data("TAG".utf8))
equal("title still readable", ID3.read(path: legacyPath)?.title, "Kept")

section("ID3 rewrite does not duplicate the tag")
try! ID3.write(simple, to: legacyPath)
try! ID3.write(simple, to: legacyPath)
let rewritten = try! Data(contentsOf: URL(fileURLWithPath: legacyPath))
check("audio still exactly one copy",
      rewritten.suffix(audioMarker.count) == audioMarker)
var idCount = 0
for i in 0..<(rewritten.count - 3) where rewritten[i] == 0x49
    && rewritten[i + 1] == 0x44 && rewritten[i + 2] == 0x33 && i < 16 {
    idCount += 1
}
equal("one ID3 header at the front", idCount, 1)

section("TagService write merges rather than erases")
let mergePath = makeFile(named: "merge.mp3", audio: audioMarker)
var base = ID3.Tag()
base.title = "Original Title"
base.album = "Original Album"
base.genre = "Rock"
try! ID3.write(base, to: mergePath)

let partial = Track()
partial.path = mergePath
partial.title = "New Title"        // album and genre deliberately left blank
try! TagService.write(partial)

let merged = ID3.read(path: mergePath)!
equal("supplied field overwritten", merged.title, "New Title")
equal("blank field left alone", merged.album, "Original Album")
equal("other blank field left alone", merged.genre, "Rock")

section("Numeric genre references resolve on read")
// "(17)" is the ID3v1 index for Rock; some old taggers still write it.
var numericGenre = ID3.Tag()
numericGenre.genre = "(17)"
let genrePath = makeFile(named: "genre.mp3", audio: audioMarker)
try! ID3.write(numericGenre, to: genrePath)
equal("(17) becomes Rock", ID3.read(path: genrePath)?.genre, "Rock")

section("Reader survives a file with no tag at all")
let bare = makeFile(named: "bare.mp3", audio: audioMarker)
let bareTag = ID3.read(path: bare)
check("returns an empty tag, not nil", bareTag != nil)
equal("no title", bareTag?.title, "")

section("Reader survives garbage")
let garbage = makeFile(named: "garbage.mp3",
                       audio: Data("ID3not-really-a-tag-at-all".utf8) + audioMarker)
check("does not crash or hang", ID3.read(path: garbage) != nil)

// MARK: - Link normalising

section("YtDlp.normalizeForProbe")
let radio = YtDlp.normalizeForProbe(
    "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=RDdQw4w9WgXcQ&start_radio=1")
equal("radio mix collapses to one video", radio.url,
      "https://www.youtube.com/watch?v=dQw4w9WgXcQ")
check("radio mix flagged single", radio.singleVideo)

let playlist = YtDlp.normalizeForProbe(
    "https://www.youtube.com/playlist?list=PLabcdefghijklmnop")
check("real playlist expands", !playlist.singleVideo)

let watchWithList = YtDlp.normalizeForProbe(
    "https://www.youtube.com/watch?v=abc&list=PLabcdefghij")
check("watch link with a real playlist keeps it", !watchWithList.singleVideo)

let nonYouTube = YtDlp.normalizeForProbe("https://soundcloud.com/artist/track")
equal("other hosts pass through", nonYouTube.url, "https://soundcloud.com/artist/track")

// MARK: - Metadata cleaning and scoring

section("MetadataClient.clean")
equal("official video stripped",
      MetadataClient.clean(artist: "", title: "Chop Suey! (Official Video)").title,
      "Chop Suey!")
equal("bracket noise stripped",
      MetadataClient.clean(artist: "", title: "Toxicity [Official Audio]").title,
      "Toxicity")
equal("topic suffix stripped",
      MetadataClient.clean(artist: "System of a Down - Topic", title: "x").artist,
      "System of a Down")

section("MetadataClient.splitVideoTitle")
let split = MetadataClient.splitVideoTitle("System of a Down - Chop Suey! (Official Video)")
equal("artist", split.artist, "System of a Down")
equal("title", split.title, "Chop Suey!")

section("MetadataClient.merge")
func candidate(_ source: String, score: Double, album: String = "",
               genre: String = "", isrc: String = "") -> MatchCandidate {
    let c = MatchCandidate()
    c.source = source
    c.score = score
    c.title = "Chop Suey!"
    c.artist = "System of a Down"
    c.album = album
    c.genre = genre
    c.isrc = isrc
    return c
}

let mergedResult = MetadataClient.merge([
    candidate("iTunes", score: 90, album: "Toxicity", genre: "Metal"),
    candidate("Deezer", score: 84, album: "Toxicity", isrc: "USSM10101234"),
    candidate("Nonsense", score: 20, album: "Wrong Album", genre: "Polka"),
])!
equal("keeps the best album", mergedResult.album, "Toxicity")
equal("keeps the best genre", mergedResult.genre, "Metal")
equal("borrows ISRC from the close second", mergedResult.isrc, "USSM10101234")
check("ignores the distant third", mergedResult.mergedFrom == ["Deezer"],
      "contributors were \(mergedResult.mergedFrom)")

// MARK: - Track model

section("Track.missingFields")
let empty = Track()
empty.path = "/tmp/x.mp3"
check("a bare track is incomplete", !empty.isComplete)
check("art counted as missing", empty.missingFields().contains("art"))

let full = Track()
full.title = "t"; full.artist = "a"; full.album = "b"; full.year = "2002"
full.genre = "g"; full.trackNumber = "3"; full.bpm = "120"; full.hasArt = true
check("a fully tagged track is complete", full.isComplete,
      "missing: \(full.missingText)")

let djayOnly = Track()
djayOnly.djayBpm = 128.4
equal("djay BPM shown when tags have none", djayOnly.displayBpm, "128")

section("Track.durationText")
let timed = Track()
timed.durationSeconds = 245
equal("under an hour", timed.durationText, "4:05")
timed.durationSeconds = 3725
equal("over an hour", timed.durationText, "1:02:05")

// MARK: - Config

section("AppConfig round trip")
var config = AppConfig()
config.libraryFolder = "/Users/test/Music"
config.filenamePattern = "{artist} - {title}"
let encoded = try! JSONEncoder().encode(config)
let decoded = try! JSONDecoder().decode(AppConfig.self, from: encoded)
equal("library folder", decoded.libraryFolder, config.libraryFolder)
equal("pattern", decoded.filenamePattern, config.filenamePattern)
equal("defaults preserved", decoded.bitrate, "320")

// MARK: - Result

print("\n\(checks - failures)/\(checks) checks passed")
if failures > 0 {
    print("\(failures) FAILED")
    exit(1)
}
print("all good")
