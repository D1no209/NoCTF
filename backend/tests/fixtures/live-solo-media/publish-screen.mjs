import { Room, VideoSource, VideoFrame, VideoBufferType, LocalVideoTrack, TrackSource, TrackPublishOptions, VideoCodec, dispose } from '@livekit/rtc-node'

// Synthetic video only: exercises real RTP/encoding/Egress without capturing any user's desktop.
const width = 640, height = 360, room = new Room(), source = new VideoSource(width, height)
let track
try {
  await room.connect(process.env.LIVEKIT_URL, process.env.LIVEKIT_TOKEN, { autoSubscribe: false })
  track = LocalVideoTrack.createVideoTrack('screen', source)
  const options = new TrackPublishOptions({ source: TrackSource.SOURCE_SCREENSHARE, videoCodec: VideoCodec.H264, simulcast: false })
  const publication = await room.localParticipant.publishTrack(track, options)
  console.log(`TRACK_READY:${publication.sid}`)
  const end = Date.now() + Number(process.env.DURATION_SECONDS ?? 90) * 1000
  const pixels = new Uint8Array(width * height * 4)
  let frame = 0
  while (Date.now() < end) {
    for (let pixel = 0; pixel < width * height; pixel++) {
      const x = pixel % width, y = Math.floor(pixel / width)
      pixels[pixel * 4] = (x + frame * 3) % 256
      pixels[pixel * 4 + 1] = (y + frame) % 256
      pixels[pixel * 4 + 2] = frame % 256
      pixels[pixel * 4 + 3] = 255
    }
    source.captureFrame(new VideoFrame(pixels, width, height, VideoBufferType.RGBA))
    frame++
    await new Promise(resolve => setTimeout(resolve, 67))
  }
}
finally {
  await room.disconnect()
  await track?.close()
  await source.close()
  await dispose()
}
