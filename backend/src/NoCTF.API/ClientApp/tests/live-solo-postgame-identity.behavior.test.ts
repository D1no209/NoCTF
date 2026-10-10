import { expect, test } from 'bun:test'
import { computed, effectScope, onScopeDispose, ref, watch } from 'vue'
import { PostgamePreviewLease } from '../app/features/live-solo/postgame-preview-lease'
import { postgameQuestion, postgameReviewTarget } from '../app/features/live-solo/postgame-state'
import { latestWriteUpVersion, writeUpStatusKey } from '../app/features/writeups/writeup-state'

const source=await Bun.file(new URL('../app/features/live-solo/useLiveSoloPostgameWriteUps.ts',import.meta.url)).text()
const compiled=new Bun.Transpiler({loader:'ts'}).transformSync(source).replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm,'').replace(/export function /g,'function ')
function fixture() {
  const user=ref<{userId:string}|null>({userId:'participant'}),scope=effectScope(),downloads:string[]=[]
  let grant=async()=>({data:{previewUrl:'/private-pdf',downloadUrl:'/download'}})
  const root={id:'root',source:'Official',publishedVersionId:'version',published:{id:'version',number:1,format:'Pdf',state:'Approved'}}
  const dependencies={ref,computed,watch,onScopeDispose,onMounted:()=>{},onBeforeRouteLeave:()=>{},window:{removeEventListener:()=>{}},useNow:()=>ref(new Date()),useAuth:()=>({user}),useLocale:()=>({t:(key:string)=>key}),
    useRoute:()=>({params:{id:'competition',matchId:'match',roundId:'round',questionId:'question'}}),useRouter:()=>({push:async()=>{}}),
    competitionContextKey:Symbol(),inject:()=>({competition:ref({administrationRole:null}),refresh:async()=>{}}),canManageLiveSolo:()=>false,
    listLiveSoloPostgameQuestions:async()=>({data:{items:[{id:'question',roundId:'round',roundNumber:1,title:'Question'}]}}),
    listLiveSoloWriteUps:async()=>({data:{items:[root],access:{settings:{enabled:true}}}}),
    getLiveSoloWriteUpContent:async()=>({data:{format:'Pdf'}}),prepareLiveSoloWriteUpBrowserAccess:()=>grant(),startLiveSoloBrowserDownload:(url:string)=>downloads.push(url),
    message:(key:string)=>({key}),parseApiError:(cause:unknown)=>({displayMessage:cause}),PostgamePreviewLease,postgameQuestion,postgameReviewTarget,latestWriteUpVersion,writeUpStatusKey}
  const factory=new Function('deps',`const {${Object.keys(dependencies).join(',')}}=deps;${compiled};return useLiveSoloPostgameWriteUps`)(dependencies)
  const state=scope.run(()=>factory())!
  return {user,state,downloads,scope,grant:(value:typeof grant)=>{grant=value}}
}
test('identity loss cancels postgame PDF access and clears body, metadata and editing content synchronously',async()=>{
  const f=fixture()
  try {
    await f.state.reload();expect(f.state.pdf.value).toBe('/private-pdf');expect(f.state.content.value?.format).toBe('Pdf')
    f.state.markdown.value='private unsaved text';f.user.value=null
    expect(f.state.pdf.value).toBeNull();expect(f.state.content.value).toBeNull();expect(f.state.listing.value).toBeNull()
    expect(f.state.markdown.value).toBe('');expect(f.state.questionOptions.value).toEqual([])
  }finally{f.scope.stop()}
})
test('an old-identity PDF download grant is discarded after account switching',async()=>{
  const f=fixture();let complete!:(value:{data:{previewUrl:string;downloadUrl:string}})=>void
  try {
    await f.state.reload();f.grant(()=>new Promise(resolve=>{complete=resolve}))
    const pending=f.state.download();f.user.value={userId:'other'};complete({data:{previewUrl:'/old',downloadUrl:'/old-download'}});await pending
    expect(f.downloads).toEqual([]);expect(f.state.pdf.value).toBeNull()
  }finally{f.scope.stop()}
})
