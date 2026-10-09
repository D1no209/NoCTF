import { computed, onScopeDispose, ref, shallowRef, watch, type Ref } from 'vue'
import type { Room } from 'livekit-client'
import { joinLiveSoloMedia } from '../../../api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMediaResponse as Media } from '../../../api'
import { parseLiveSoloError } from '../live-solo-errors'
import { message, type UiMessage } from '../../../utils/i18n'

export function useLiveSoloScreen(competitionId: Ref<string>, matchId: Ref<string>, media: Ref<Media | null>, staff: Ref<boolean>,
  dependencies = { loadSdk: () => import('livekit-client'), authorize: joinLiveSoloMedia }) {
  const connected = ref(false), connecting = ref(false), publishing = ref(false), capturePending = ref(false)
  const error = ref<UiMessage | null>(null), localStream = shallowRef<MediaStream | null>(null)
  const remoteStreams = shallowRef<ReadonlyMap<string, MediaStream>>(new Map())
  let room: Room | null = null, request = 0, disposed = false, stoppingShare = false
  const eligible = computed(() => media.value?.state === 'Ready' && !!media.value.generation)
  async function disconnect() {
    request++; const previous = room; room = null
    connected.value = false; connecting.value = false; publishing.value = false; capturePending.value = false
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
      next = new sdk.Room({ adaptiveStream: true, dynacast: true })
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
        }
      })
      current.on(sdk.RoomEvent.Reconnecting, () => { if (id === request) connected.value = false })
      current.on(sdk.RoomEvent.Reconnected, () => { if (id === request) connected.value = true })
      current.on(sdk.RoomEvent.Disconnected, () => {
        if (id !== request) return
        if (publishing.value) error.value = message('liveSolo.screen.interrupted')
        connected.value = false; publishing.value = false
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
    const current = room, id = request
    capturePending.value = true; error.value = null
    try {
      const publication = await current.localParticipant.setScreenShareEnabled(true, { audio: false, systemAudio: 'exclude',
        contentHint: 'detail', resolution: { width: 1920, height: 1080, frameRate: 15 } })
      if (id !== request || disposed) { publication?.track?.stop(); await current.disconnect(true); return }
      const track = publication?.track?.mediaStreamTrack
      if (track) { localStream.value = new MediaStream([track]); publishing.value = true }
    }
    catch (cause) {
      if (id === request) error.value = cause instanceof DOMException && cause.name === 'NotAllowedError'
        ? message('liveSolo.screen.permissionDenied') : parseLiveSoloError(cause, message('liveSolo.error.media')).displayMessage
    }
    finally { if (id === request) capturePending.value = false }
  }
  async function stopSharing() {
    stoppingShare = true
    try {
      if (room) await room.localParticipant.setScreenShareEnabled(false)
      localStream.value?.getTracks().forEach(track => track.stop()); localStream.value = null; publishing.value = false
    }
    catch (cause) { error.value = parseLiveSoloError(cause, message('liveSolo.error.media')).displayMessage }
    finally { stoppingShare = false }
  }
  watch([() => media.value?.generation, eligible, matchId, staff], () => { void disconnect() })
  onScopeDispose(() => { disposed = true; void disconnect() })
  return { connected, connecting, publishing, capturePending, error, localStream, remoteStreams, eligible, connect, share, stopSharing, disconnect }
}
