import { expect,test } from 'bun:test'
import { computed,ref } from 'vue'
const compiled=new Bun.Transpiler({loader:'ts'}).transformSync(await Bun.file(new URL('../app/features/routes/admin/platform/useAdminPlatformExperimentsPage.ts',import.meta.url)).text())
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm,'').replace(/export function /g,'function ')
function fixture(){
  const patches:unknown[]=[]
  const configuration={experimentalFeatures:{ctfPatchVerificationEnabled:false},liveSoloVideo:{maximumWidth:1280,maximumHeight:720,maximumFramesPerSecond:10,maximumBitrateBitsPerSecond:1_000_000},
    liveSoloVideoRules:{minimumWidth:320,maximumWidth:3840,minimumHeight:180,maximumHeight:2160,minimumFramesPerSecond:1,maximumFramesPerSecond:30,minimumBitrateBitsPerSecond:128_000,maximumBitrateBitsPerSecond:8_000_000}}
  const deps={ref,computed,onMounted:()=>{},usePlatform:()=>({refresh:async()=>{}}),Beaker:()=>null,RefreshCw:()=>null,toast:{success:()=>{},error:()=>{}},describeMessage:(key:string)=>({key}),parseApiError:(cause:unknown)=>({displayMessage:cause}),
    adminPlatformGetConfiguration:async()=>({data:configuration}),adminPlatformPatchConfiguration:async(input:{body:{liveSoloVideo:object}})=>{patches.push(input);Object.assign(configuration.liveSoloVideo,input.body.liveSoloVideo);return {data:configuration}}}
  const factory=new Function('deps',`const {${Object.keys(deps).join(',')}}=deps;${compiled};return useAdminPlatformExperimentsPage`)(deps)
  return {state:factory(),patches}
}
test('platform video UI presents Kbit/s and sends bit/s only for the selected section',async()=>{
  const f=fixture();await f.state.load();expect(f.state.videoKbps.value).toBe(1000)
  f.state.videoKbps.value=512;await f.state.saveVideo()
  expect(f.patches).toEqual([{body:{liveSoloVideo:{maximumWidth:1280,maximumHeight:720,maximumFramesPerSecond:10,maximumBitrateBitsPerSecond:512_000}}}])
  expect(f.state.videoDirty.value).toBe(false)
})
test('invalid sizes frame rates and bitrate cannot submit platform mutations',async()=>{
  const f=fixture();await f.state.load()
  f.state.videoWidth.value=641;await f.state.saveVideo();expect(f.patches).toEqual([])
  f.state.videoWidth.value=640;f.state.videoFps.value=31;await f.state.saveVideo();expect(f.patches).toEqual([])
  f.state.videoFps.value=5;f.state.videoKbps.value=127;await f.state.saveVideo();expect(f.patches).toEqual([])
})
