import { expect,test } from 'bun:test'
import type { LocalVideoTrack } from 'livekit-client'
import { constrainScreen,constrainSender,fitScreen,sampleScreen,screenVideoOptions } from '../app/features/live-solo/media/screen-video-policy'
const policy={maximumWidth:1280,maximumHeight:720,maximumFramesPerSecond:10,maximumBitrateBitsPerSecond:1_000_000}
test('capture bounds preserve wide and portrait aspect ratios without enlarging lower resolution input',()=>{
  expect(fitScreen(1920,1080,1280,720)).toEqual({width:1280,height:720})
  expect(fitScreen(1080,1920,1280,720)).toEqual({width:405,height:720})
  expect(fitScreen(640,360,1280,720)).toEqual({width:640,height:360})
  expect(screenVideoOptions(policy).publish).toMatchObject({simulcast:false,backupCodec:false,degradationPreference:'balanced'})
})
test('ignored browser constraints and extra sender layers are refused rather than treated as compliance',async()=>{
  const native={getSettings:()=>({width:1920,height:1080,frameRate:30}),applyConstraints:async()=>{}} as unknown as MediaStreamTrack
  await expect(constrainScreen(native,policy)).rejects.toThrow('exceeds')
  const track={sender:{getParameters:()=>({encodings:[{active:true},{active:true}]})},simulcastCodecs:new Map()} as unknown as LocalVideoTrack
  await expect(constrainSender(track,policy)).rejects.toThrow('additional')
})
test('observed bitrate is measured from counters and missing track dimensions remain unverified',async()=>{
  let bytes=1000,timestamp=1000
  const track={mediaStreamTrack:{getSettings:()=>({})},codec:'h264',getSenderStats:async()=>[{timestamp,bytesSent:bytes}]} as unknown as LocalVideoTrack
  const first=await sampleScreen(track,null);expect(first.bitrateBitsPerSecond).toBeNull();expect(first.frameWidth).toBeNull()
  bytes+=125000;timestamp+=1000
  const next=await sampleScreen(track,first);expect(next.bitrateBitsPerSecond).toBe(1_000_000);expect(next.captureWidth).toBeNull()
})
