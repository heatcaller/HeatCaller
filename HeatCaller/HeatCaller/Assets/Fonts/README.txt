HeatCaller - bundled typefaces
================================

Drop font files in THIS folder and the app loads them at startup. Nothing
needs installing on the machine, which is the point: a track PC you cannot
install software on still renders the app properly.

WHAT TO PUT HERE
----------------

From https://fonts.google.com/specimen/Oxanium  -> "Get font" -> download,
then open the "static" folder inside the zip and copy:

    Oxanium-Regular.ttf
    Oxanium-Bold.ttf

From https://fonts.google.com/specimen/Barlow -> same, from "static":

    Barlow-Medium.ttf        <- the one body text actually uses
    Barlow-Regular.ttf
    Barlow-Bold.ttf

Also copy the OFL.txt out of each zip. Two files with the same name, so
rename them:

    OFL-Oxanium.txt
    OFL-Barlow.txt

Both families are licensed under the SIL Open Font License 1.1. That allows
commercial use and redistribution inside an application, on the condition
that the licence text ships alongside the fonts - which is what those two
files are for. Do not remove them.

ONLY REGULAR AND BOLD
---------------------

This matters and it is not obvious. GDI understands four styles: regular,
bold, italic, bold-italic. A file named Oxanium-SemiBold.ttf does not
register as "Oxanium, semibold" - it registers as a family of its OWN
called "Oxanium SemiBold", and asking for Oxanium in bold will never find
it. The same goes for the variable-weight file (Oxanium[wght].ttf) in the
top level of the zip.

So: use the files from the "static" folder, and only the Regular and Bold
cuts. Extra weights in here are harmless but do nothing.

Barlow-Medium.ttf is the deliberate exception. Barlow Regular is too light
for near-white text on a dark panel - it reads as thin and grey rather than
as text. Medium arrives as a family of its own ("Barlow Medium"), which is
exactly what body copy wants; bold still comes from the real Barlow family.
Leave it out and body text falls back to Barlow Regular.

IF THE FOLDER IS EMPTY
----------------------

The app falls back on its own: Oxanium -> Bahnschrift -> Segoe UI for
display text, Barlow -> Segoe UI for body text, Cascadia Mono -> Consolas
for the clock. It will look like it did before rather than break. The same
applies if you would rather just install the fonts in Windows normally -
installed families are found too.
