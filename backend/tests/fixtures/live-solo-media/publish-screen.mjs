import { Room, VideoSource, VideoFrame, VideoBufferType, LocalVideoTrack, TrackSource, TrackPublishOptions, VideoCodec, dispose } from '@livekit/rtc-node'

// Synthetic video only: exercises real RTP/encoding/Egress without capturing any user's desktop.
const width = 640, height = 360, room = new Room(), source = new VideoSource(width, height)
const framesPerSecond=Number(process.env.VIDEO_FPS??10),bitrateBitsPerSecond=Number(process.env.VIDEO_BITRATE??1_000_000)
let track
try {
  await room.connect(process.env.LIVEKIT_URL, process.env.LIVEKIT_TOKEN, { autoSubscribe: false })
  track = LocalVideoTrack.createVideoTrack('screen', source)
  const options = new TrackPublishOptions({ source: TrackSource.SOURCE_SCREENSHARE, videoCodec: VideoCodec.H264, simulcast: false,
    videoEncoding:{maxBitrate:BigInt(bitrateBitsPerSecond),maxFramerate:framesPerSecond} })
  const publication = await room.localParticipant.publishTrack(track, options)
  console.log(`TRACK_READY:${publication.sid}`)
  const end = Date.now() + Number(process.env.DURATION_SECONDS ?? 90) * 1000
  const pixels = new Uint8Array(width * height * 4)
  let frame = 0
  let previous=null,nextSample=Date.now()+5000
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
    if(Date.now()>=nextSample){
      const stats=await room.getRtcStats(),rows=stats.publisherStats.filter(row=>row.stats.case==='outboundRtp').map(row=>row.stats.value).filter(row=>row.stream?.kind==='video')
      const bytes=rows.reduce((sum,row)=>sum+Number(row.sent?.bytesSent??0n),0),at=Date.now()
      console.log('VIDEO_SAMPLE:'+JSON.stringify({at,width:rows[0]?.outbound?.frameWidth??null,height:rows[0]?.outbound?.frameHeight??null,
        fps:rows[0]?.outbound?.framesPerSecond??null,bitrate:previous?(bytes-previous.bytes)*8000/(at-previous.at):null,bytes,encodings:rows.length}))
      previous={at,bytes};nextSample=at+5000
    }
    await new Promise(resolve => setTimeout(resolve, 1000/framesPerSecond))
  }
}
finally {
  await room.disconnect()
  await track?.close()
  await source.close()
  await dispose()
}
