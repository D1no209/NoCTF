import { describe, expect, test } from 'bun:test'
import { effectScope, nextTick, ref } from 'vue'
import * as sdk from 'livekit-client'
import { useLiveSoloScreen } from '../app/features/live-solo/media/useLiveSoloScreen'
import type { joinLiveSoloMedia } from '../app/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMediaResponse as Media } from '../app/api'

function fixture(staff = false, delayConnect?: Promise<void>) {
  const actions: unknown[][] = [], handlers = new Map<string, (...args: unknown[]) => void>()
  const original=Object.getOwnPropertyDescriptor(globalThis,'MediaStream')
  Object.defineProperty(globalThis,'MediaStream',{configurable:true,value:class {constructor(private tracks:MediaStreamTrack[]){}getTracks(){return this.tracks}}})
  const policy={maximumWidth:1280,maximumHeight:720,maximumFramesPerSecond:10,maximumBitrateBitsPerSecond:1_000_000,programmeBitrateBitsPerSecond:1_000_000}
  let currentTrack:sdk.LocalVideoTrack|null=null
  function makeTrack(){
    const settings={width:640,height:360,frameRate:15}
    const native={getSettings:()=>({...settings}),applyConstraints:async(options:MediaTrackConstraints)=>{
      actions.push(['constraints',options]);settings.width=Math.min(settings.width,(options.width as ConstrainULongRange).max??settings.width)
      settings.height=Math.min(settings.height,(options.height as ConstrainULongRange).max??settings.height);settings.frameRate=Math.min(settings.frameRate,(options.frameRate as ConstrainDoubleRange).max??settings.frameRate)
    },stop:()=>{actions.push(['stop'])}}
    currentTrack={kind:sdk.Track.Kind.Video,codec:'h264',mediaStreamTrack:native,simulcastCodecs:new Map(),
      sender:{getParameters:()=>({encodings:[{active:true}]}),setParameters:async(params:unknown)=>{actions.push(['encoding',params])}},
      getSenderStats:async()=>[],stop:()=>native.stop()} as unknown as sdk.LocalVideoTrack
    return currentTrack
  }
  class Room {
    constructor(options:unknown){actions.push(['room',options])}
    localParticipant = { setScreenShareEnabled: async (...args: unknown[]) => { actions.push(['stop-share', ...args]); return undefined },
      publishTrack:async(track:sdk.LocalVideoTrack,options:unknown)=>{actions.push(['publish',options]);return {track}} }
    on(event: string, callback: (...args: unknown[]) => void) { handlers.set(event, callback); return this }
    async connect(...args: unknown[]) { actions.push(['connect', ...args]); await delayConnect }
    async disconnect(stop: boolean) { actions.push(['disconnect', stop]) }
  }
  const media = ref<Media | null>({ generation: 'generation-one', state: 'Ready', participantsMayViewOpponents: false,videoPolicy:policy })
  const scope = effectScope()
  const dependencies = {
    loadSdk: async () => ({ ...sdk, Room: Room as unknown as typeof sdk.Room,createLocalScreenTracks:(async(options:unknown)=>{actions.push(['capture',options]);return [makeTrack()]}) as typeof sdk.createLocalScreenTracks }),
    authorize: (async (input: unknown) => { actions.push(['authorize', input]); return { data: { token: 'test-private-token', serverUrl: 'wss://media.invalid' } } }) as typeof joinLiveSoloMedia,
  }
  const screen = scope.run(() => useLiveSoloScreen(ref('competition'), ref('match'), media, ref(staff), dependencies))!
  const stop=()=>{scope.stop();if(original)Object.defineProperty(globalThis,'MediaStream',original);else Reflect.deleteProperty(globalThis,'MediaStream')}
  return { screen, media, scope, actions, handlers,stop,track:()=>currentTrack }
}

describe('LiveSolo manual screen lifecycle', () => {
  test('connecting requests the publisher grant but does not request capture, camera, audio or data', async () => {
    const f = fixture()
    try {
      await f.screen.connect()
      expect(f.screen.connected.value).toBe(true)
      expect(f.screen.publishing.value).toBe(false)
      expect(f.actions.some(action => action[0] === 'capture')).toBe(false)
      expect(f.actions[0]?.[1]).toMatchObject({ body: { generation: 'generation-one', role: 'Publisher' } })
      await f.screen.share()
      expect(f.actions.find(action => action[0] === 'capture')).toMatchObject(['capture', { audio: false, systemAudio: 'exclude',resolution:{width:1280,height:720,frameRate:10} }])
      expect(f.actions.find(action=>action[0]==='publish')).toMatchObject(['publish',{videoCodec:'h264',simulcast:false,backupCodec:false,screenShareEncoding:{maxBitrate:1_000_000,maxFramerate:10}}])
    }
    finally { f.stop() }
  })
  test('staff joins subscribe-only role and cannot invoke screen capture', async () => {
    const f = fixture(true)
    try {
      await f.screen.connect(); await f.screen.share()
      expect(f.actions[0]?.[1]).toMatchObject({ body: { role: 'Judge' } })
      expect(f.actions.some(action => action[0] === 'capture')).toBe(false)
    }
    finally { f.stop() }
  })
  test('a generation change during connection closes the old room and cannot become connected afterwards', async () => {
    let resolve!: () => void
    const f = fixture(false, new Promise<void>(done => { resolve = done }))
    try {
      const pending = f.screen.connect()
      await new Promise(done => setTimeout(done, 0))
      f.media.value = { generation: 'generation-two', state: 'Ready' }; await nextTick()
      resolve(); await pending
      expect(f.screen.connected.value).toBe(false)
      expect(f.actions.some(action => action[0] === 'disconnect')).toBe(true)
    }
    finally { f.stop() }
  })
  test('room retirement clears connection state and stale callbacks cannot restore it', async () => {
    const f = fixture()
    try {
      await f.screen.connect()
      f.media.value = null; await nextTick()
      f.handlers.get(sdk.RoomEvent.Reconnected)?.()
      expect(f.screen.connected.value).toBe(false)
      expect(f.screen.localStream.value).toBeNull()
    }
    finally { f.stop() }
  })
  test('re-sharing, reconnection and changed tracks retain one bounded encoding',async()=>{
    const f=fixture()
    try {
      await f.screen.connect();await f.screen.share();await f.screen.stopSharing();await f.screen.share()
      f.handlers.get(sdk.RoomEvent.Reconnected)?.();await new Promise(done=>setTimeout(done,0))
      expect(f.actions.filter(x=>x[0]==='capture')).toHaveLength(2)
      for(const action of f.actions.filter(x=>x[0]==='publish'))expect(action[1]).toMatchObject({simulcast:false,backupCodec:false,videoEncoding:{maxBitrate:1_000_000,maxFramerate:10}})
      expect(f.actions.filter(x=>x[0]==='encoding').length).toBeGreaterThanOrEqual(3)
      f.track()!.mediaStreamTrack={...f.track()!.mediaStreamTrack,getSettings:()=>({width:640,height:360,frameRate:10}),applyConstraints:async()=>{f.actions.push(['replacement-constrained'])}} as unknown as MediaStreamTrack
      f.handlers.get(sdk.RoomEvent.Reconnected)?.();await new Promise(done=>setTimeout(done,0))
      expect(f.actions.some(x=>x[0]==='replacement-constrained')).toBe(true)
    }finally{f.stop()}
  })
  test('lower platform limits update the connected publisher without retiring its room',async()=>{
    const f=fixture()
    try {
      await f.screen.connect();await f.screen.share()
      f.media.value={...f.media.value!,videoPolicy:{maximumWidth:320,maximumHeight:180,maximumFramesPerSecond:5,maximumBitrateBitsPerSecond:256_000,policyStamp:'lower-policy'}}
      await nextTick();await new Promise(done=>setTimeout(done,0))
      expect(f.screen.connected.value).toBe(true);expect(f.screen.publishing.value).toBe(true)
      expect(f.actions.filter(x=>x[0]==='encoding').at(-1)?.[1]).toMatchObject({encodings:[{maxBitrate:256_000,maxFramerate:5}]})
      expect(f.actions.some(x=>x[0]==='disconnect')).toBe(false)
    }finally{f.stop()}
  })
})
