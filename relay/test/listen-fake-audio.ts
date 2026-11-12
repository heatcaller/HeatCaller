/**
 * Just enough Web Audio for the listen-along player (2 Nov 2026), which
 * plays through an AudioContext since it started joining the room's stream
 * together. A decoded buffer remembers the base64 it came from, so a test
 * can say which documents were played, in what order, starting when and
 * from where - and which were stopped.
 *
 * atob is the identity here: the tests' "audio" is a word, not base64, and
 * the player only ever hands what atob gives it straight to the decoder.
 */
export class FakeBuffer {
  readonly sampleRate = 44100;

  constructor(
    readonly tag: string,
    readonly length: number,
  ) {}

  get duration(): number {
    return this.length / this.sampleRate;
  }
}

export interface Start {
  readonly tag: string;
  readonly at: number;
  readonly offset: number;
  readonly length: number;
  stopped: boolean;
}

export class FakeAudioWorld {
  /** The audio clock, which the test moves. */
  now = 0;

  /** How long every decoded buffer is, in samples. */
  bufferLength = 44100;

  /** Whether the code running now is inside the press. */
  inPress = false;

  /** The rate the player's context says it runs at - none unless a test sets it (11 Nov 2026). */
  contextRate: number | undefined = undefined;

  /** Every decode, and where it happened: on the player's context, or an offline one at this rate. */
  readonly decodedOn: { tag: string; on: "context" | number }[] = [];

  readonly starts: Start[] = [];
  readonly contexts: { inPress: boolean; resumedInPress: boolean; state: string }[] = [];
  readonly elements: { src: string; loop: boolean; paused: boolean; playedInPress: boolean }[] = [];

  /** What was played, in the order it was started. */
  played(): string[] {
    return this.starts.map((one) => one.tag);
  }

  /** The globals the page's script needs for its sound. */
  globals(): Record<string, unknown> {
    const world = this;

    class AudioContext {
      readonly destination = {};
      private readonly record = { inPress: world.inPress, resumedInPress: false, state: "suspended" };

      constructor() {
        world.contexts.push(this.record);
      }

      get state(): string {
        return this.record.state;
      }

      set state(value: string) {
        this.record.state = value;
      }

      get currentTime(): number {
        return world.now;
      }

      get sampleRate(): number | undefined {
        return world.contextRate;
      }

      resume(): Promise<void> {
        this.record.resumedInPress ||= world.inPress;
        this.record.state = "running";
        return Promise.resolve();
      }

      suspend(): Promise<void> {
        this.record.state = "suspended";
        return Promise.resolve();
      }

      decodeAudioData(bytes: ArrayBuffer): Promise<FakeBuffer> {
        const tag = String.fromCharCode(...new Uint8Array(bytes));
        world.decodedOn.push({ tag, on: "context" });
        return Promise.resolve(new FakeBuffer(tag, world.bufferLength));
      }

      createBufferSource() {
        let started: Start | undefined;

        return {
          buffer: null as FakeBuffer | null,
          onended: null as (() => void) | null,
          connect(): void {},
          start(at: number, offset: number, length: number): void {
            started = { tag: this.buffer?.tag ?? "", at, offset, length, stopped: false };
            world.starts.push(started);
          },
          stop(): void {
            if (started !== undefined) {
              started.stopped = true;
            }
          },
        };
      }
    }

    class Audio {
      private readonly record = { src: "", loop: false, paused: true, playedInPress: false };

      constructor() {
        world.elements.push(this.record);
      }

      get src(): string {
        return this.record.src;
      }

      set src(value: string) {
        this.record.src = value;
      }

      get loop(): boolean {
        return this.record.loop;
      }

      set loop(value: boolean) {
        this.record.loop = value;
      }

      play(): Promise<void> {
        this.record.paused = false;
        this.record.playedInPress ||= world.inPress;
        return Promise.resolve();
      }

      pause(): void {
        this.record.paused = true;
      }
    }

    // Only for decoding, as the player uses it (11 Nov 2026). A harness that
    // does not put it on the page's window gets a page without one.
    class OfflineAudioContext {
      constructor(
        readonly numberOfChannels: number,
        readonly length: number,
        readonly sampleRate: number,
      ) {}

      decodeAudioData(bytes: ArrayBuffer): Promise<FakeBuffer> {
        const tag = String.fromCharCode(...new Uint8Array(bytes));
        world.decodedOn.push({ tag, on: this.sampleRate });
        return Promise.resolve(new FakeBuffer(tag, world.bufferLength));
      }
    }

    return { AudioContext, Audio, OfflineAudioContext, atob: (text: string) => text };
  }
}
