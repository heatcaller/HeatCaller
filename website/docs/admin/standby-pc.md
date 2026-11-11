# Standby PC

A standby PC is a second laptop at the track that keeps a copy of the timing laptop's race data. If the timing laptop dies mid-meeting, you press **Take Over** on the standby, plug the decoder into it, and carry on from the last copy.

It's set up on the **Backup & Restore** page, on the **Standby PC** card, on both laptops.

!!! note "Taking over is always done by hand"
    The standby never takes over by itself. Only you can decide the timing laptop really can't carry on. Two laptops both deciding they're in charge would be far worse than half a minute spent pressing a button.

## What it copies

Every 15 seconds the standby fetches a fresh copy of the club's race data from the timing laptop:

- drivers, transponders and classes
- the race day, heats, mains and the night plan
- check-ins and money at the desk
- results and race history, championships and seasons
- the race in progress, so after **Take Over** HeatCaller offers to carry on with it

These stay as the standby has them: its settings (decoder port, voice, looks), its voice files, and its own keys and PINs. **Keys, tokens and PINs are never in the copy.** Music, photos and race audio aren't copied either.

Copies are kept as zip files in `Documents\HeatCaller Backups\Standby Copies`, the newest and the one before it. The standby's own data isn't touched until you press **Take Over**.

## Keeping it safe

- **One way only.** The timing laptop hands out a copy and the standby only reads it. Nothing on the standby can change the timing laptop.
- **The track's own network only.** The copy is only given to PCs on the same network, such as a `192.168.x.x` address. It never goes over the internet.
- **A PIN.** The standby has to give the **Standby PIN**: at least 8 letters or digits, typed the same on both laptops. After 20 wrong PINs in five minutes the timing laptop stops accepting it for five minutes and says so in its console.
- **Off until you turn it on.** A timing laptop that hasn't ticked **Let a standby PC copy this one** gives nothing to anybody.

The copy goes over the network like the other Local Screens pages, so anyone who can watch the track's network traffic could read it. Use a network only the club controls, ideally a cable or the club's own router, rather than a venue's open Wi-Fi.

## Setting it up

**On the timing laptop:**

1. On **Local Screens**, press **Start Server**. The standby copies through the same server the phones use.
2. On **Backup & Restore**, in the **Standby PC** card, type a **Standby PIN** of at least 8 letters or digits.
3. Tick **Let a standby PC copy this one (this is the timing PC)**. The line under it shows the address to type on the standby, for example `192.168.1.20:8080`.

**On the standby laptop:**

1. Install HeatCaller and connect it to the same network as the timing laptop.
2. On **Backup & Restore**, in the **Standby PC** card, type the same **Standby PIN**, and type the timing laptop's address in **Timing PC address:**.
3. Tick **This PC is a standby - copy the timing PC every 15 seconds**.

The standby's card, and the **Next Up** bar on its Race Control page, then show how fresh its copy is, for example "Standby PC: copy of TIMING-PC from 12 s ago." If no fresh copy has arrived for a minute, the line says **BEHIND** and why. The timing laptop's card shows when a standby last copied it.

**Copy Now** fetches a copy straight away.

## Taking over

When the timing laptop can't carry on:

1. On the standby, open **Backup & Restore** and press **Take Over...** in the **Standby PC** card.
2. The question says how old the copy is. Anything the timing laptop saved after that isn't on the standby, so check the last race's result once you're running.
3. Press **Yes**. HeatCaller saves the standby's current data to a safety backup, puts the copy in place and restarts.
4. Connect the decoder to the standby. If a race was running, HeatCaller offers to carry on with it.

Take Over is refused while a race or practice is running on the standby, and while a backup is being written.

After taking over, the standby is the timing PC. It stops copying, and nothing is ever copied over it again, even if the old timing laptop comes back to life. To make it a standby again later, for example once the night is over and the old laptop is fixed, press **Make This a Standby Again...**.

### After taking over

- **Keys and PINs** are the standby's own, not the timing laptop's. If the standby was never set up for the club website, email or racer PINs, set those up on it, or restore a full backup made with a [backup password](backups.md#the-backup-password) after the night.
- **The safety backup** of the standby's data from before the takeover is in the `Pre-Restore Safety Backups` folder, like any restore's. See [Restoring a backup](backups.md#restoring-a-backup).

## What it doesn't do yet

- It doesn't take over by itself. That's deliberate for now.
- It doesn't send each lap as it happens. A copy is at most about 15 seconds old, so laps from the last few seconds of a race that was running when the timing laptop died may be missing. Check that race's result after taking over.
- It doesn't copy music, photos, race audio or the finish camera's pictures.

## Related pages

- [Backups](backups.md)
- [Local Screens](../screens/phone-and-web.md)
- [When the internet drops](../online/index.md#when-the-internet-drops)
