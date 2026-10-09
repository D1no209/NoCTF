import { expect, test } from 'bun:test'
import { computed, effectScope, onScopeDispose, ref, watch, nextTick } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { playableRecording, recordingActions, recordingActionKey, recordingFailureKey } from '../app/features/live-solo/recording-policy'

const source=await Bun.file(new URL('../app/features/live-solo/useLiveSoloRecordings.ts',import.meta.url)).text()
const compiled=new Bun.Transpiler({loader:'ts'}).transformSync(source).replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm,'').replace(/export function /g,'function ')
function fixture() {
  const user=ref<{userId:string}|null>({userId:'admin'}),scope=effectScope(),downloads:string[]=[]
  let grant=async()=>({data:{downloadUrl:'/download'}})
  const route={params:{id:'competition',matchId:'match',recordingId:'record'},query:{staff:'1'}}
  const dependencies={ref,computed,watch,onScopeDispose,onMounted:()=>{},useRoute:()=>route,useRouter:()=>({replace:async()=>{},push:async()=>{}}),useAuth:()=>({user}),useOffsetPagination,
    listLiveSoloRecordings:async()=>({data:{items:[{id:'record',state:'Completed',byteLength:100,concurrencyStamp:'stamp',fileUrl:'/recording'}],total:1,canJudge:true,canPublish:true}}),
    listLiveSoloRecordingDecisions:async()=>({data:{items:[{id:'decision',action:'Hold'}]}}),prepareLiveSoloRecordingPreview:async()=>({data:{previewUrl:'/private-preview'}}),
    prepareLiveSoloRecordingDownload:()=>grant(),startLiveSoloBrowserDownload:(url:string)=>downloads.push(url),
    message:(key:string)=>({key}),parseLiveSoloError:(cause:unknown)=>({displayMessage:cause}),ApiError:Error,
    playableRecording,recordingActions,recordingActionKey,recordingFailureKey}
  const factory=new Function('deps',`const {${Object.keys(dependencies).join(',')}}=deps;${compiled};return useLiveSoloRecordings`)(dependencies)
  const state=scope.run(()=>factory())!
  return {user,state,downloads,scope,grant:(value:typeof grant)=>{grant=value}}
}
test('logout immediately removes recording source, private metadata and staff actions',async()=>{
  const f=fixture()
  try {
    await f.state.load();await nextTick();await f.state.retry()
    expect(f.state.source.value).toBe('/private-preview');expect(f.state.options.value).toHaveLength(1);expect(f.state.actions.value.length).toBeGreaterThan(0)
    f.state.begin('Hold');expect(f.state.dialog.value).toBe(true)
    f.user.value=null
    expect(f.state.source.value).toBeNull();expect(f.state.options.value).toEqual([]);expect(f.state.history.value).toEqual([])
    expect(f.state.actions.value).toEqual([]);expect(f.state.dialog.value).toBe(false)
  }finally{f.scope.stop()}
})
test('a grant returned after identity changes cannot start a private recording download',async()=>{
  const f=fixture();let complete!:(value:{data:{downloadUrl:string}})=>void
  try {
    await f.state.load();await nextTick()
    f.grant(()=>new Promise(resolve=>{complete=resolve}))
    const pending=f.state.download();f.user.value={userId:'other'};complete({data:{downloadUrl:'/old-private-download'}});await pending
    expect(f.downloads).toEqual([]);expect(f.state.options.value).toEqual([])
  }finally{f.scope.stop()}
})
