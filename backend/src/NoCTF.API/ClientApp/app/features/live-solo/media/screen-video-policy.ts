import type { ScreenShareCaptureOptions, TrackPublishOptions, LocalVideoTrack } from 'livekit-client'
import type { NoCtfapiEndpointsLiveSoloLiveSoloPublisherVideoPolicyResponse as Policy } from '../../../api'

export function screenVideoOptions(policy: Policy) {
  const {maximumWidth:width,maximumHeight:height,maximumFramesPerSecond:fps,maximumBitrateBitsPerSecond:bitrate}=policy
  if(![width,height,fps,bitrate].every(value=>typeof value==='number'&&Number.isSafeInteger(value)&&value>0))
    throw new Error('Missing video budget')
  const encoding={maxBitrate:bitrate!,maxFramerate:fps!}
  const publish:TrackPublishOptions={videoCodec:'h264',simulcast:false,backupCodec:false,screenShareSimulcastLayers:[],
    screenShareEncoding:encoding,videoEncoding:encoding,degradationPreference:'balanced'}
  const capture:ScreenShareCaptureOptions={audio:false,systemAudio:'exclude',contentHint:'text',
    resolution:{width:width!,height:height!,frameRate:fps!}}
  return {capture,publish,width:width!,height:height!,fps:fps!,bitrate:bitrate!}
}

export function fitScreen(width:number,height:number,maximumWidth:number,maximumHeight:number) {
  const ratio=Math.min(1,maximumWidth/width,maximumHeight/height)
  return {width:Math.max(1,Math.floor(width*ratio)),height:Math.max(1,Math.floor(height*ratio))}
}

export async function constrainScreen(track:MediaStreamTrack,policy:Policy) {
  const budget=screenVideoOptions(policy),before=track.getSettings()
  const fitted=before.width&&before.height?fitScreen(before.width,before.height,budget.width,budget.height):{width:budget.width,height:budget.height}
  await track.applyConstraints({width:{max:fitted.width},height:{max:fitted.height},frameRate:{max:budget.fps}})
  const after=track.getSettings()
  if(after.width&&after.width>budget.width||after.height&&after.height>budget.height||after.frameRate&&after.frameRate>budget.fps)
    throw new Error('Capture exceeds video budget')
  // A browser may leave dimensions unavailable; that is unverified, never an inferred pass.
  if(before.width&&before.height&&after.width&&after.height
    && Math.abs(after.width/after.height-before.width/before.height)>2/after.height)
    throw new Error('Capture changed source aspect ratio')
}

export async function constrainSender(track:LocalVideoTrack,policy:Policy) {
  const sender=track.sender;if(!sender)return
  const budget=screenVideoOptions(policy),parameters=sender.getParameters()
  if(parameters.encodings.length!==1||track.simulcastCodecs.size>0)throw new Error('Unexpected additional video encoding')
  const encoding=parameters.encodings[0]!;encoding.maxBitrate=budget.bitrate;encoding.maxFramerate=budget.fps
  const settings=track.mediaStreamTrack.getSettings()
  if(settings.width&&settings.height)encoding.scaleResolutionDownBy=Math.max(1,settings.width/budget.width,settings.height/budget.height)
  parameters.degradationPreference='balanced';await sender.setParameters(parameters)
}

export type ScreenVideoSample={timestamp:number;bytesSent:number;frameWidth:number|null;frameHeight:number|null;framesPerSecond:number|null;
  bitrateBitsPerSecond:number|null;activeEncodings:number;captureWidth:number|null;captureHeight:number|null;captureFramesPerSecond:number|null;codec:string|null}

export async function sampleScreen(track:LocalVideoTrack,previous:ScreenVideoSample|null):Promise<ScreenVideoSample> {
  const settings=track.mediaStreamTrack.getSettings(),stats=await track.getSenderStats()
  const timestamp=Math.max(0,...stats.map(row=>row.timestamp)),bytesSent=stats.reduce((sum,row)=>sum+(row.bytesSent??0),0)
  const elapsed=previous?timestamp-previous.timestamp:0
  return {timestamp,bytesSent,frameWidth:stats[0]?.frameWidth??null,frameHeight:stats[0]?.frameHeight??null,framesPerSecond:stats[0]?.framesPerSecond??null,
    bitrateBitsPerSecond:elapsed>0&&previous&&bytesSent>=previous.bytesSent?(bytesSent-previous.bytesSent)*8000/elapsed:null,
    activeEncodings:stats.length,captureWidth:settings.width??null,captureHeight:settings.height??null,captureFramesPerSecond:settings.frameRate??null,
    codec:track.codec??null}
}
