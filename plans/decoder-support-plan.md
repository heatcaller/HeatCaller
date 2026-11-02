# HeatCaller: decoders we can add without buying one (plan, 27 Oct 2026)

## Status (1 Nov 2026): BUILT, all Beta (branch decoder-support)

Built: groundwork, MyLaps P3, ZRound open protocol, Cano / RCHourglass,
OpenStint (native, over ZeroMQ) and EasyLap / Robitronic serial. Not built:
the maybes past the serial EasyLap, the needs-hardware list, Carrera.

Built differently from the plan:
- OpenStint uses HeatCaller's own ZeroMQ subscriber (ZmtpSubscriber, ZMTP
  3.0 written from the spec), not NetMQ. NetMQ is LGPL-3.0 and brings six
  packages with it; it is used by the tests only, as a real ZeroMQ
  publisher to check the subscriber against.
- Decoders 3 to 7 can be the line but not a split loop.
- Laps are timed on the decoder's own clock mapped onto the app's
  (DecoderClockMap), not on arrival as the I-Lap is.
- The RCHourglass wiki's worked example calls quality 0x2A "52%"; it is 42.

Checks: the P3 reader read the amb-p3-parser project's real records with
the values its tests expect (scratch check, not committed - GPL); 51
new tests (DecoderProtocolTests, StreamDecoderClientTests); the rehearsal 3 EasyLap night reads the same over the serial
path as over USB. Still to do before any loses "(Beta)": a real evening on
each decoder, with its raw capture read (DecoderConnection.Beta).


Plan only. No code changed. Checked against asherizm/heatcaller at 3e07147.

## Today
- EasyLap over USB (DirectDecoderClient, CP2110 HID wrapping a 38400 8N1 UART stream; tested on the real unit).
- I-Lap over serial (ILapClient; written before the hardware, never run on a real unit).
- DecoderConnection says "next unused number is 3"; OtherDecoder() and several labels assume exactly two decoders.
- ROADMAP still says MyLaps has "nothing to build against". That is out of date (see P3 below).

## Confident: documented format plus a way to test with no device

### 1. MyLaps / AMB P3 (AMBrc, RC4, also TranX3, ChipX, BIB per ammc)
- Source: datagutten/amb-p3-parser (GPL; protocol from vlad_vy's hobbytalk posts), with real captured records in its tests, including an RC4 passing, with expected decoded values (transponder 7404583, hits 16, decoder id 0x405FC). It also ships a TCP decoder emulator that replays captures. ammc-wasm (npm) and ghidinelli/ambmylapstiming confirm P3 is the protocol of AMBrc, RC4 and DR5.
- Format: 0x8E start, 0x8F end, 0x8D escape (next byte minus 0x20). Header: version, length (LE16), CRC (LE16), flags, record type (LE16; 1 = PASSING, 2 = STATUS). Then TLV fields: id, length, value. PASSING: 0x01 passing number, 0x03 transponder, 0x04 RTC time (8 bytes, microseconds), 0x05 strength, 0x06 hits, 0x08 flags, 0x81 decoder id.
- Connection: TCP client to the decoder, port 5403 (decoder serves up to 4 connections). Also offer a COM port for serial P3.
- Test with no device: our own C# test vectors written from the published values, plus a built-in P3 simulator that serves passings on localhost:5403.
- Catches: the CRC algorithm isn't confirmed (I tried six common CRC-16 variants against the samples; none matched), so read without rejecting on CRC and log mismatches. Some newer MyLaps decoders ship with P3 switched off. Before telling a club it works, borrow one club RC4 decoder for an evening.

### 2. ZRound open protocol
- Source: ZRound wiki page lapcounters:protocols:open, and OpenStint's bridge-zround.py, which shows the decoder side acts as the TCP server and sends laps as %L<tx>,<hex ms>&.
- Format: every message is % ... &. PC sends %C& (decoder answers %A& within 500 ms), %I& race start (clock to zero), %F& race stop. Decoder sends %T<hex ms>& clock and %L<tx hex>[,]<hex ms>& laps.
- Connection: serial 19200 8N1, or TCP to the decoder's IP and port (OpenStint bridge default 5001).
- Who it reaches: RiCino (DIY), anything else ZRound lists as using the open protocol, and OpenStint through its bridge.
- Test with no device: built-in simulator, plus OpenStint's own simulator.py feeding bridge-zround.

### 3. Cano / RCHourglass (DIY decoders; ZRound and FlipSide read them)
- Source: Howard Cano's own post in rctech "Lap Timing Decoder" thread (page 34), with sample lines, and the RCHourglass wiki Protocol page.
- Format: one ASCII line per pass ending CR LF: 6 hex digits transponder, 8 hex digits time in quarter milliseconds since power-up. RCHourglass mode adds -id hits quality voltage temperature (hex). Power-up line "COPYRIGHT 2013 HOWARD W. CANO" to ignore.
- Connection: serial. RCHourglass wiki says 57600 8N1; offer the baud picker like I-Lap.
- Test with no device: Cano's published sample lines and the wiki's worked examples as tests, plus a simulator.

### 4. OpenStint (open-source decoder for MyLaps RC3 / RC4-hybrid transponders with an RTL-SDR)
- Source: github.com/zsellera/openstint, docs/decoder-protocol.md (GPL).
- Format: ZeroMQ PUB/SUB text messages, default port 5556. Passing: "P <ms timestamp> <OPN|AMB> <transponder id> <rssi> <hits> <pass duration us> ...". Also T (time sync) and S (status).
- Connection: TCP via ZeroMQ (NetMQ, a managed .NET library).
- Test with no device: its own integrations/simulator.py, and replay of captured radio samples.
- Note: once ZRound protocol is built, OpenStint already works through its bridge. Native support only adds hits and signal and drops the bridge.

## Maybe: partly documented, or a strong guess
- EasyLap serial / Robitronic serial: our own capture shows the USB box is a UART stream at 38400 8N1 inside HID, so the serial models very likely send the same bytes. Cheap: reuse EasyLapFramer on a COM port and replay our EasyLap captures through a virtual COM pair. Needs one owner to confirm.
- Robitronic USB: our code already calls the CP2110 path "EasyLap / Robitronic". May already work if the USB IDs match; add other IDs once someone reports theirs.
- Legacy AMB ASCII serial (AMB20, AMBrc emulation in RCHourglass): only partly published ("@" + transponder + time + hits + CR LF at 9600). RCHourglassManager source may fill the gaps.
- TranX serial: RCHourglass emulates it, but no format found.
- Invelion YR9700 UHF RFID: vendor command manuals exist for this reader family; it's for karting and running, so low value for RC.

## Needs hardware (left out)
Trackmate Iridium, iRace, Vostok decoder, Orange Lap RC, Core Speedway, Kyosho IC Tag, LapZ, GiroZ, LapMonitor, MyLaps X2. No public format found. ImmersionRC LapRF is documented but is drone timing.

## Future (added 29 Oct 2026, on Asher's ask)
- Carrera Digital 124/132 Control Unit (slot cars). Each car has an ID chip and the Control Unit reports which car crossed the line. Hobby apps already read it over its serial link (Black Box or PC-Unit port; the Bluetooth AppConnect adapter speaks the same protocol). Community-documented protocol and open-source readers exist (carreralib and OpenLap); confirm the details and licenses before building. Slot cars are a new market beside RC, so it sits after the RC decoders.

## Build order
0. Groundwork: let the decoder list grow past two (DecoderConnection numbers 3+, OtherDecoder, hard-coded "EasyLap"/"I-Lap" labels, Connection page fields for host/port), one shared serial/TCP reader with the reconnect, timeouts and raw capture ILapClient already has, and a "Simulated decoder" source per protocol so each can be rehearsed at home.
1. MyLaps P3 (most clubs; ROADMAP's top reason a club can't switch).
2. ZRound open protocol (most solid spec; brings RiCino and OpenStint along).
3. Cano / RCHourglass.
4. OpenStint native.
5. EasyLap / Robitronic serial (cheap reuse).
Then the maybes, each only when a beta club or owner can confirm.

## Licensing
P3 parser and OpenStint are GPL; RCHourglass is non-commercial. Write HeatCaller's readers from the formats, don't copy their code, and write our own test vectors.
