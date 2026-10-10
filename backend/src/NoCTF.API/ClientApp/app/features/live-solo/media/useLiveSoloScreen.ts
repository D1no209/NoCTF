import { computed, onScopeDispose, ref, shallowRef, watch, type Ref } from 'vue'
import type { Room, LocalVideoTrack } from 'livekit-client'
import { joinLiveSoloMedia } from '../../../api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMediaResponse as Media } from '../../../api'
import { parseLiveSoloError } from '../live-solo-errors'
import { message, type UiMessage } from '../../../utils/i18n'
import { screenVideoOptions, constrainScreen, constrainSender, sampleScreen, type ScreenVideoSample } from './screen-video-policy'

export function useLiveSoloScreen(competitionId: Ref<string>, matchId: Ref<string>, media: Ref<Media | null>, staff: Ref<boolean>,
  dependencies = { loadSdk: () => import('livekit-client'), authorize: joinLiveSoloMedia }) {
  const connected = ref(false), connecting = ref(false), publishing = ref(false), capturePending = ref(false)
  const error = ref<UiMessage | null>(null), localStream = shallowRef<MediaStream | null>(null)
  const remoteStreams = shallowRef<ReadonlyMap<string, MediaStream>>(new Map())
  const videoSample=shallowRef<ScreenVideoSample|null>(null)
  let room: Room | null = null, request = 0, disposed = false, stoppingShare = false
  let videoTrack:LocalVideoTrack|null=null,sdkModule:Awaited<ReturnType<typeof dependencies.loadSdk>>|null=null,videoTimer:ReturnType<typeof setTimeout>|undefined
  function clearVideo(){if(videoTimer)clearTimeout(videoTimer);videoTimer=undefined;videoTrack=null;videoSample.value=null}
  async function observeVideo(id:number){
    const track=videoTrack,policy=media.value?.videoPolicy
    if(disposed||id!==request||!track||!policy)return
    try {
      await constrainScreen(track.mediaStreamTrack,policy);await constrainSender(track,policy)
      const sample=await sampleScreen(track,videoSample.value)
      if(disposed||id!==request||track!==videoTrack)return
      if(sample.codec!=='h264')throw new Error('Unsupported recording codec')
      videoSample.value=sample
    }catch {
      if(id===request&&!disposed){error.value=message('liveSolo.screen.budgetUnavailable');await stopSharing();return}
    }
    if(!disposed&&id===request&&track===videoTrack)videoTimer=setTimeout(()=>{void observeVideo(id)},2000)
  }
  const eligible = computed(() => media.value?.state === 'Ready' && !!media.value.generation)
  async function disconnect() {
    request++; const previous = room; room = null
    connected.value = false; connecting.value = false; publishing.value = false; capturePending.value = false
    clearVideo();sdkModule=null
    localStream.value?.getTracks().forEach(track => track.stop())
    localStream.value = null; remoteStreams.value = new Map()
    await previous?.disconnect(true)
  }
  async function connect() {
    if (!eligible.value || connecting.value || connected.value) return
    const id = ++request, generation = media.value!.generation!
    connecting.value = true; error.value = null
    let next: Room | null = null
    try {
      const [sdk, authorization] = await Promise.all([dependencies.loadSdk(), dependencies.authorize({
        path: { competitionId: competitionId.value, matchId: matchId.value },
        body: { generation, role: staff.value ? 'Judge' : 'Publisher' },
      })])
      if (disposed || id !== request || media.value?.generation !== generation) return
      if (authorization.error || !authorization.data?.serverUrl || !authorization.data.token)
        throw parseLiveSoloError(authorization.error, message('liveSolo.error.media'))
      const policy=media.value?.videoPolicy;if(!policy)throw new Error('Missing video policy')
      const options=screenVideoOptions(policy)
      // Defer peer creation until server ICE configuration is known, instead of mutating an initial offer's configuration.
      next = new sdk.Room({ adaptiveStream: true, dynacast: true, singlePeerConnection: false, publishDefaults:options.publish })
      sdkModule=sdk
      const current = next
      current.on(sdk.RoomEvent.TrackSubscribed, (track, publication, participant) => {
        if (id !== request || publication.source !== sdk.Track.Source.ScreenShare || track.kind !== sdk.Track.Kind.Video) return
        const streams = new Map(remoteStreams.value); streams.set(participant.identity, new MediaStream([track.mediaStreamTrack])); remoteStreams.value = streams
      })
      current.on(sdk.RoomEvent.TrackUnsubscribed, (_track, publication, participant) => {
        if (id !== request || publication.source !== sdk.Track.Source.ScreenShare) return
        const streams = new Map(remoteStreams.value); streams.delete(participant.identity); remoteStreams.value = streams
      })
      current.on(sdk.RoomEvent.ParticipantDisconnected, participant => {
        if (id !== request) return
        const streams = new Map(remoteStreams.value); streams.delete(participant.identity); remoteStreams.value = streams
      })
      current.on(sdk.RoomEvent.LocalTrackUnpublished, publication => {
        if (id === request && publication.source === sdk.Track.Source.ScreenShare) {
          if (publishing.value && !stoppingShare) error.value = message('liveSolo.screen.interrupted')
          publishing.value = false; localStream.value = null
          clearVideo()
        }
      })
      current.on(sdk.RoomEvent.Reconnecting, () => { if (id === request) connected.value = false })
      current.on(sdk.RoomEvent.Reconnected, () => {if(id===request){connected.value=true;if(videoTrack){if(videoTimer)clearTimeout(videoTimer);void observeVideo(id)}}})
      current.on(sdk.RoomEvent.Disconnected, () => {
        if (id !== request) return
        if (publishing.value) error.value = message('liveSolo.screen.interrupted')
        connected.value = false; publishing.value = false
        clearVideo()
        localStream.value?.getTracks().forEach(track => track.stop()); localStream.value = null; remoteStreams.value = new Map(); room = null
      })
      room = current
      await current.connect(authorization.data.serverUrl, authorization.data.token, { autoSubscribe: staff.value || media.value?.participantsMayViewOpponents === true })
      if (disposed || id !== request || media.value?.generation !== generation) { await current.disconnect(true); return }
      connected.value = true
    }
    catch (cause) {
      await next?.disconnect(true)
      if (id === request) { room = null; error.value = parseLiveSoloError(cause, message('liveSolo.error.media')).displayMessage }
    }
    finally { if (id === request) connecting.value = false }
  }
  // Invoked directly by a user gesture. Connecting does not request screen/camera/microphone permissions.
  async function share() {
    if (!room || !connected.value || staff.value || capturePending.value || publishing.value) return
    const current = room, id = request,sdk=sdkModule,policy=media.value?.videoPolicy
    if(!sdk||!policy)return
    capturePending.value = true; error.value = null
    let captured:Awaited<ReturnType<typeof sdk.createLocalScreenTracks>>=[]
    try {
      const options=screenVideoOptions(policy)
      captured=await sdk.createLocalScreenTracks(options.capture)
      if(captured.length!==1||captured[0]?.kind!==sdk.Track.Kind.Video)throw new Error('Unexpected screen capture sources')
      const native=captured[0].mediaStreamTrack
      await constrainScreen(native,policy)
      if(id!==request||disposed){captured.forEach(track=>track.stop());return}
      const publication=await current.localParticipant.publishTrack(captured[0],{...options.publish,source:sdk.Track.Source.ScreenShare})
      if (id !== request || disposed) { publication?.track?.stop(); await current.disconnect(true); return }
      const track = publication?.track?.mediaStreamTrack
      if (track) { localStream.value = new MediaStream([track]); publishing.value = true;videoTrack=publication.track as LocalVideoTrack;await observeVideo(id) }
    }
    catch (cause) {
      captured.forEach(track=>track.stop())
      if (id === request) error.value = cause instanceof DOMException && cause.name === 'NotAllowedError'
        ? message('liveSolo.screen.permissionDenied') : parseLiveSoloError(cause, message('liveSolo.error.media')).displayMessage
    }
    finally { if (id === request) capturePending.value = false }
  }
  async function stopSharing() {
    clearVideo()
    stoppingShare = true
    try {
      if (room) await room.localParticipant.setScreenShareEnabled(false)
      localStream.value?.getTracks().forEach(track => track.stop()); localStream.value = null; publishing.value = false
    }
    catch (cause) { error.value = parseLiveSoloError(cause, message('liveSolo.error.media')).displayMessage }
    finally { stoppingShare = false }
  }
  watch([() => media.value?.generation, eligible, matchId, staff], () => { void disconnect() })
  watch(()=>media.value?.videoPolicy?.policyStamp,()=>{if(videoTrack){if(videoTimer)clearTimeout(videoTimer);void observeVideo(request)}})
  onScopeDispose(() => { disposed = true; void disconnect() })
  const videoBudgetMessage=computed(()=>media.value?.videoPolicy?message('liveSolo.screen.videoBudget',{
    width:media.value.videoPolicy.maximumWidth??'—',height:media.value.videoPolicy.maximumHeight??'—',fps:media.value.videoPolicy.maximumFramesPerSecond??'—',
    bitrate:(media.value.videoPolicy.maximumBitrateBitsPerSecond??0)/1000}):null)
  const videoObservedMessage=computed(()=>videoSample.value?message('liveSolo.screen.videoObserved',{
    width:videoSample.value.frameWidth??'—',height:videoSample.value.frameHeight??'—',fps:videoSample.value.framesPerSecond??'—',
    bitrate:videoSample.value.bitrateBitsPerSecond===null?'—':Math.round(videoSample.value.bitrateBitsPerSecond/1000),streams:videoSample.value.activeEncodings}):null)
  return { connected, connecting, publishing, capturePending, error, localStream, remoteStreams,videoSample,videoBudgetMessage,videoObservedMessage,eligible, connect, share, stopSharing, disconnect }
}
