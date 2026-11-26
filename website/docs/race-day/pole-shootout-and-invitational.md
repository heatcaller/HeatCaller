# Pole Shootout and Invitational

Extra ways to run a class's finals. A **Pole Shootout** sends the top qualifiers out one at a time to fight for pole. An **Invitational (Reedy-style format)** races the whole field in a series of mains scored on points. **Lower mains in legs** runs the B, C and lower mains in legs like a double or triple A-Main. Each is off until you turn it on.

## Before you start

All three are set per class. On the **Classes** page, edit the class and click **Finals Formats** in the row of buttons under its settings. Tick **Run a Pole Shootout after qualifying**, **Run the finals as an Invitational** or **Run the lower mains in legs too**, and click **OK**. Like the other buttons there, the change is kept when you click **Save** on the class.

The runs and mains are built when you build the class's finals: **Build the finals from qualifying** on **Race Day → Next Steps**, or **Build the Finals from Qualifying...** on **More**. See [Seed the mains](program-and-mains.md#seed-the-mains).

## Pole Shootout

After qualifying, the top qualifiers each run alone, one car at a time. The slowest of them goes first and the top qualifier goes last. Each run is an out lap and then the timed laps. The fastest run takes pole.

| Setting | What it does | Default |
|---|---|---|
| **Run a Pole Shootout after qualifying** | Builds the shootout's runs with the finals. | Off |
| **Drivers:** | How many of the top qualifiers run, from 2 to 10. It's never more than the class's **Drivers per main:**. | 5 |
| **Timed laps:** | Laps timed after the out lap, from 1 to 3. The best one counts. | 1 |

### How it runs

- Each run is its own round on the program, **Pole Shootout Run 1**, **Pole Shootout Run 2** and so on, before the class's mains. The **Kind** column says **Pole Shootout**.
- A run has one driver. Race Control runs it like any race, with the out lap free, and the run ends after the timed laps. A run also stops after a few minutes, for a car that never comes round.
- A run's time is the best of its timed laps, plus any time penalty the driver was given. A disqualified run, or one with no timed lap, has no time.
- If a qualifying result is corrected, or a late heat is recorded, before any run has gone out, the runs follow the new qualifying order. Once a run has been raced, the shootout is left as it is.

### The grid it makes

When the last run is recorded, the shootout's drivers fill the front of the A-Main grid in shootout order. Everyone else keeps their qualifying place behind them. A driver whose run had no time keeps their qualifying place among the shootout drivers, behind everyone who set a time.

The grid is set once. If you change the A-Main's drivers by hand afterwards, HeatCaller leaves your change alone. For a double or triple A-main the shootout sets every leg's grid. For an Invitational it sets the grid of **Invitational Main 1**.

### What the announcer says

After each run, with **Unofficial result - who won, who they beat and the fastest lap** ticked on **Voice → AI Commentary**, the announcer says the time and where it puts the driver, for example "Sam, 12.345 - quickest so far." After the last run it says who has pole, for example "Sam takes pole in the Pole Shootout, 12.345.", and the race recap mentions it too.

### Results

- Each run's own result is in **Results** like any race.
- The event report has a **Pole Shootout** table for the class: each driver's time, the place they qualified and who has pole.
- On the club website, each run's result shows the shootout order so far under it, and the final order once every run is in.

## Invitational (Reedy-style format)

An invited field races a series of mains. Every driver is in every main. Each finish scores points, the best mains count, and fewest points wins.

| Setting | What it does | Default |
|---|---|---|
| **Run the finals as an Invitational** | Builds the mains as an Invitational instead of A, B and C mains. The class's **Drivers per main:**, **Bump up:** and **A-main:** aren't used. | Off |
| **Mains:** | How many mains, from 2 to 12. | 5 |
| **Mains that count:** | How many of each driver's best mains count. It can't be more than **Mains:**. | 4 |
| **Field:** | The most drivers invited, taken from the top of the seed order. 0 invites everyone entered in the class. | 0 |
| **Points:** | **1st = 1 point (Reedy Race scale)**: 1st scores 1, 2nd scores 2 and so on. **The class's qualifying points scale**: the class's **Top qualifier scores:** setting, so a class on IFMAR or EFRA points gives the winner 0. | 1st = 1 point |
| **Invert the grid (points leader starts at the back)** | After main 1, the driver leading on points starts at the back. | Off |
| **Seed order:** | **Seed Order** opens the order drivers are seeded in when they have no qualifying result. | Roster order |

### The seed order

Main 1's grid is the seed order:

1. If the class has qualified, the qualifying order comes first.
2. Then anyone without a qualifying result, in the order you set with **Seed Order**.
3. Then anyone else in the class, in roster order.

So an Invitational can run with no qualifying at all: the seed order decides main 1's grid. In the **Seed Order** window, select a driver and click **To the Top**, **Move Up** or **Move Down**. **Roster Order** clears your order. Click **OK** to keep it.

### Scoring

1. Each main is scored on finishing place, using the **Points:** setting.
2. A driver who didn't start a main, or was disqualified from it, scores the field plus one (500 on the IFMAR scale, the field on the EFRA scale).
3. Each driver's best mains count, as many as **Mains that count:**. Fewest points wins.

Ties are broken by the best finish in any single main, then by the better finish in the last main run.

### How it runs

- The mains are added to the program as **Invitational Main 1**, **Invitational Main 2** and so on. The **Kind** column says **Invitational**.
- Main 1 starts in seed order. When a main is recorded, the next main's grid is set from the points so far: the leader at the front, or at the back with **Invert the grid** ticked.
- When a main is recorded, the next main's grid is set from the points. If an earlier main's result is corrected later, the next main's grid is set again, as long as that main hasn't started and nobody has changed its drivers by hand. Once you change a main's drivers by hand, HeatCaller leaves your grid alone.
- There are no bump-ups: the whole field is already in every main.

### What the announcer says

After each main the announcer says who leads, for example "After main 2 of 5, Sam leads the Invitational on 3 points." After the last main it says who won, for example "Sam wins the Invitational, best four of five mains." The race recap mentions the standings too.

### Results

- Each main's own result is in **Results** like any race.
- The event report has an **Invitational (Reedy-style format)** table for the class: each driver's points and their place and points in every main, with any dropped main marked. It says **standings** until the last main is in.
- On the club website, each main's result shows the standings with that main in, and the final result after the last one.

## Lower mains in legs

With a double or triple A-Main (**A-main:** in the class settings), tick **Run the lower mains in legs too** to run the B-Main, C-Main and the rest in legs as well. Each lower main runs the same number of legs as the A-Main and counts the same number of them. With a single A-Main, or with the Invitational on, it does nothing. It's off unless you tick it.

- The legs are added as **B-Main Leg 1**, **B-Main Leg 2** and so on. Every leg of a main runs before the main above it: all the C-Main's legs, then all the B-Main's, then the A-Main's.
- Each lower main is scored like the A-Main legs: 1st scores 1 point, a leg not started or a DQ scores the field plus one, the best legs count and fewest points wins. Ties go to the best single-leg finish, then the last leg. See [Double and triple A-mains](program-and-mains.md#double-and-triple-a-mains).
- Bump-ups come from a main's overall result, once every one of its legs is in. **Move the Top Finishers Up...** names the main's overall result ("Bump 2 drivers from the B-Main overall up into..."), and the drivers go into every leg of the main above that hasn't run, the way bump-ups into the A-Main legs work. Until the last leg is in, the bump-up says which leg hasn't run yet.
- Once a main's last leg is recorded, the announcer says who won it overall, for example "Sam wins the B-Main overall, best two of three legs."
- The event report has a **B-Main overall** table (and one for each main run in legs) beside **A-Main overall**.
- On the club website, from a main's second leg on, each leg's result shows that main's overall standings so far. This is the same for the A-Main's legs.

## Tips

- Turn the Pole Shootout on before you build the finals. If you turn it on afterwards, build the finals again.
- If the finals are built again after a shootout run has been raced, the shootout is kept and its result sets the new A-Main's grid.
