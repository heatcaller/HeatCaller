# Emailing racer PINs

HeatCaller can email each racer their personal check-in PIN (see [Personal PINs](check-in.md#personal-pins)). The emails are sent straight from the timing laptop through your club's own mailbox, such as a Gmail or iCloud account. Nothing goes through a cloud service: the PINs leave the laptop only inside the emails themselves, and neither the PINs nor the mailbox password are ever written to the log.

You set this up once in the **Email (for racer PINs)** card on the **Club Website** page.

The same mailbox also sends drivers their night after racing, when the club offers it, a link to their own page when a driver asks for one from **Me** (with **Let drivers email themselves a link to their page (uses the club's email, below)** ticked on the **Online check-in** card), and a note to a driver's old address when the desk approves a new one from their phone. See [Drivers' nights by email](setup.md#drivers-nights-by-email), [Opening your night with your PIN](index.md#opening-your-night-with-your-pin) and [Driver requests](driver-requests.md#approving-a-change-to-their-details).

## Before you start

- A mailbox for the club, ideally one used only for the club (for example a free Gmail account).
- An **app password** for that mailbox. Gmail and iCloud don't let other programs sign in with your normal password; you create a separate app password in the account's security settings. See [Creating an app password](#creating-an-app-password).
- Online check-in and personal PINs switched on, with publishing working. The email tells racers where to use their PIN, so HeatCaller won't send PINs until there's a check-in page to point to. See [Online check-in](check-in.md).
- Email addresses on your racers' driver profiles. See [Where racers' addresses come from](#where-racers-addresses-come-from).

## Set up the club mailbox

1. On the **Club Website** page, find the **Email (for racer PINs)** card.
2. In **Provider:**, pick **Gmail**, **iCloud** or **Outlook.com**. The server and port are filled in for you. Pick **Other** to type your own.
3. In **Club email address:**, type the mailbox's full address. It's used both to sign in and as the sender.
4. In **App password:**, paste the app password. Tick **Show** to check it.
5. In **From name:**, type the name emails should come from, for example "Acme RC Club".
6. Click **Send Test Email**. It sends one message to the club's own address.
7. When the line under the button says "Test email sent to *address*. Check that mailbox.", open the mailbox and check it arrived.

Everything on this card is saved as you type (or when you leave a box). There's no Save button.

<!-- screenshot: Club Website page, Email (for racer PINs) card with Gmail selected and a successful test result -->

## Settings

| Setting | What it does | Default |
|---|---|---|
| **Provider:** | Fills in the server and port. **Gmail** is `smtp.gmail.com`, **iCloud** is `smtp.mail.me.com`, **Outlook.com** is `smtp-mail.outlook.com`, all on port 587. **Other** lets you type both. | Gmail |
| **Server:** | The mail server's address. Only editable with **Other**. | `smtp.gmail.com` |
| **Port:** | Only editable with **Other**. Use 587. | 587 |
| **Club email address:** | The mailbox's address. Used to sign in, and as the address emails come from. | (blank) |
| **App password:** | The mailbox's app password. Spaces are removed automatically, so you can paste Google's "abcd efgh ijkl mnop" style as it is. Stored encrypted on the laptop, tied to your Windows account. | (blank) |
| **From name:** | The name emails appear to come from, and the club name used in the emails. If blank, your club id is used, made readable ("acme-rc" becomes "Acme Rc"). | (blank) |

### Port 587 only

HeatCaller sends mail over port 587 with STARTTLS: the connection starts plain and is upgraded to an encrypted one before the password is sent. If the server won't upgrade, nothing is sent, so the password never crosses unencrypted.

Port 465 isn't supported. If you choose it, the page says "Port 465 isn't supported - use 587, which every mainstream provider also offers." Every mainstream provider offers 587.

## Creating an app password

The page's own hint: "Gmail/iCloud need an app password (Google Account → Security → App passwords)."

### Gmail

<!-- TODO: confirm Google's current menu wording before publishing; Google changes these pages from time to time. -->

1. Sign in to the club's Google Account in a web browser.
2. Go to **Security**. App passwords need **2-Step Verification** turned on, so turn it on first if it isn't.
3. Still in **Security**, open **App passwords**. If you can't find it, search the account settings for "App passwords".
4. Give the app password a name you'll recognize, such as "HeatCaller", and create it.
5. Google shows a 16-letter password in four groups. Copy it into **App password:** on the Club Website page. You won't be able to see it again in Google, but you can always create a new one.
6. Click **Send Test Email**.

If you change the Google Account's normal password, Google may revoke its app passwords. Create a new one and paste it in.

### iCloud

<!-- TODO: confirm Apple's current menu wording before publishing. -->

1. Sign in to your Apple Account at appleid.apple.com. The account needs two-factor authentication turned on.
2. Go to **Sign-In and Security**, then **App-Specific Passwords**.
3. Generate a new password and name it "HeatCaller".
4. Copy it into **App password:** on the Club Website page.
5. In **Club email address:**, use the iCloud Mail address.
6. Click **Send Test Email**.

### Outlook.com

!!! note
    **Outlook.com** is in the provider list, but Microsoft has been moving personal Outlook.com mailboxes away from the simple password sign-in this kind of direct sending uses. If Send Test Email keeps reporting that the sign-in was refused, even with an app password, use a Gmail or iCloud mailbox instead.

<!-- TODO: confirm the current status of SMTP password / app-password sign-in for personal Outlook.com accounts. -->

## What the PIN email says

Each racer gets their own plain-text email (plain text so it reads the same in every mail app). The subject is:

> Your check-in PIN for *club*

The body is:

```text
Hi <first name>,

Your check-in PIN for <club> is: <PIN>

To check in on race day:
  1. Open <your club's check-in page address>
  2. Enter your PIN
  3. Tap Check In

Or just scan your QR check-in with your phone's camera.

On race day you can also see your own races: open Me on the club's pages and enter the same PIN.
  <your club's Me page address>
Your phone remembers you after that.

This PIN works until <weekday day month>. You'll be sent a new one after that.

Don't share it - it checks in only you.

<club>

Timing by HeatCaller - heatcaller.com
```

- *club* is the **From name**, or your club id made readable if that's blank.
- The Me lines give your club's **Me** page, the check-in page's address ending in `/mynight` (see [Getting around](index.md#getting-around)).
- The "This PIN works until..." line is left out when **Change them:** is **Never**. The date is the last day before the next scheduled change.
- If a profile has no driver name, the greeting is just "Hi,".

The test email has the subject "Test email from *club*" and says "This is a test from HeatCaller." and "If you can read this, *club* can email racers their check-in PINs from this laptop."

## Where racers' addresses come from

For each racer, HeatCaller uses:

1. the **Email:** box on their driver profile, if it holds a real address, or
2. the **Contact:** box, if that holds nothing but a single email address (older profiles only had Contact).

Anything else, such as "Dad: 07700 900123, dad@example.com" in the Contact box, isn't treated as an address.

To add addresses in bulk, use **Import from CSV** on the **Driver Hub** page with an `Email` column (a column called `e-mail`, `email address` or `e-mail address` also works). **Download Template** on the same page gives a file with the column already in place. See [Drivers and classes](../race-day/drivers-and-classes.md).

Email addresses stay on the laptop. They're never published to the relay.

## Sending PINs

### By hand: Email PINs

1. In the **Personal PINs** card, click **Email PINs**.
2. HeatCaller asks, for example, "Email 24 racers their PIN?", says how many racers have no email address and won't get one, and estimates the time ("They go one at a time - about 1 minute. Everything else keeps working meanwhile.").
3. Click **Yes**. The state line shows "Emailing *X* of *Y*...".
4. When it's done, a summary lists who was emailed, any emails that didn't go and why, and who has no address ("Add one in the Email box on their driver profile.").

**Email PINs** sends every racer who has an address their current PIN, whether or not they've been sent it before.

### Automatically

Tick **Email automatically when PINs change** in the **Personal PINs** card. Then:

- when a new set of PINs is made (on the rotation schedule, or with **New PINs Now**), every racer with an address is emailed their new PIN
- a racer added later is sent theirs within ten minutes
- anybody who hasn't yet been sent their current PIN is sent it

HeatCaller checks at start-up and every 10 minutes after, and straight away after **New PINs Now** or switching personal PINs on. It doesn't send while a race is staging, counting down or running. Each racer is only sent their current PIN once; the **Emailed** column in **Show PINs** shows who has had theirs.

Automatic sends report to the console, for example "[pins] Emailed 3 racers their new PIN; 1 has no email." If an address is refused, that racer isn't retried automatically while the app is running. Fix the address and use **Email PINs**.

### Sending pace

Emails go one at a time, about a second and a half apart, so the provider doesn't take a sudden burst for spam. A club of 100 racers takes a few minutes. Racing and everything else carry on while it sends. Only one send runs at a time: if you try **Send Test Email** during a send, it says "Emails are being sent right now - try the test once that has finished."
## When sending fails

| Message | What it means | What to do |
|---|---|---|
| "Gmail refused the sign-in: use an app password, not the normal one." | Google rejected the address or password. The whole send stops. | Check **Club email address:** and create a new app password. |
| "*server* refused the sign-in: check the club email address, and use an app password, not the normal one." | The same, for another provider. | As above. |
| "Couldn't send through *server*:*port* - ..." | The server couldn't be reached, or wouldn't talk securely. The whole send stops. | Check the laptop's internet connection, the server and port. |
| "*server* didn't answer in time." | One message took more than a minute. The send stops. | Try again later. |
| "*Name* (*address*): ..." in the list of emails that didn't go | That one address was refused, for example a typo or a closed mailbox. The others carry on. | Fix the address on the driver profile and send again. |
| "*Name* (*address*): not a valid address" | The address can't be used. | Fix it on the driver profile. |
| "Stopped: the first three emails all failed (...)." | The same fault three times in a row, before any got through, so it's probably the server, not the racers. | Check the settings and send a test email. |
| "Port 465 isn't supported - use 587, which every mainstream provider also offers." | See [Port 587 only](#port-587-only). | Use 587. |
| "Enter the club's email address." / "Enter the mailbox's app password." / "No mail server is set." | Something's missing on the **Email (for racer PINs)** card. | Fill it in. |
| "Personal PINs are switched off." | **Give every racer their own PIN** is unticked. | Tick it. |
| "Online check-in isn't set up and switched on yet, so there is nowhere to use a PIN." | Publishing or online check-in is off. | See [Online check-in](check-in.md). |
| "Nobody with a PIN has an email address yet." | No racer has a usable address. | Add addresses to profiles, or import a CSV with an Email column. |

When a send stops partway, the message says how many went before it stopped. Those racers are marked as emailed. With automatic email on, the rest are picked up on a later pass once the problem is fixed; otherwise use **Email PINs** again.
