// HTTPS for the built-in web server was built in 0.26.1 and taken back out
// in 0.26.2, at the point where what it cost stopped being worth what it
// bought.
//
// What it did: made a self-signed certificate, wrote out the two
// administrator commands that put it in the machine store and bind it to
// the port with netsh, and ran them itself when the app was elevated.
//
// Why it went: the only thing it unlocked was scanning a badge with a
// phone's camera on the check-in page, and that needs the browser's
// BarcodeDetector API as well - which Safari does not have. So the whole
// apparatus bought one feature, on Android only, in exchange for an
// administrator step, a certificate warning on every phone, and a setting
// that silently stops the server starting if the binding is ever lost.
//
// The version in git is the reference if it comes back. The cheaper answer
// to the same problem is a real hosted origin, which arrives with a real
// certificate and needs none of this - see claude/iphone-checkin-test-plan.md.

namespace HeatCaller;
