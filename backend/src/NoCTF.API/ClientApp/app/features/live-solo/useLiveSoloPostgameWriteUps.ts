import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { useNow } from '@vueuse/core'
import { listLiveSoloPostgameQuestions, listLiveSoloWriteUps, getLiveSoloWriteUpContent, saveLiveSoloWriteUpDraft, saveLiveSoloWriteUpPdfDraft,
  submitLiveSoloWriteUp, reviewLiveSoloWriteUp, prepareLiveSoloWriteUpBrowserAccess } from '~/api'
import { getChallengeWriteUpSettings,adminUpdateChallengeWriteUpSettings } from '~/api'
import type { NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpContentResponse as Content,
  NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpListResponse as Listing, NoCtfApplicationLiveSoloResourcesLiveSoloPostgameQuestion as Question,
  NoCtfDomainChallengesWriteUpsWriteUpReviewAction as Action, NoCtfDomainChallengesWriteUpsWriteUpFormat as Format } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseApiError } from '~/utils/api-error'
import { startLiveSoloBrowserDownload } from '~/utils/download'
import { latestWriteUpVersion, writeUpStatusKey } from '~/features/writeups/writeup-state'
import { canManageLiveSolo } from './settings-draft'
import { postgameQuestion,postgameReviewTarget } from './postgame-state'
import { PostgamePreviewLease } from './postgame-preview-lease'

export function useLiveSoloPostgameWriteUps() {
  const route=useRoute(),router=useRouter(),context=inject(competitionContextKey)
  const {t}=useLocale()
  const competitionId=computed(()=>route.params.id as string),matchId=computed(()=>route.params.matchId as string)
  const questionId=computed(()=>typeof route.params.questionId==='string'?route.params.questionId:null)
  const staff=computed(()=>context?.competition.value?.administrationRole!=null),manager=computed(()=>canManageLiveSolo(context?.competition.value?.administrationRole))
  const mode=ref<'Public'|'Mine'|'Official'|'Review'>('Public'),questions=ref<Question[]>([]),listing=ref<Listing|null>(null),selected=ref<string|null>(null)
  const loading=ref(true),busy=ref(false),error=ref<UiMessage|null>(null),content=ref<Content|null>(null),pdf=ref<string|null>(null)
  const format=ref<Format>('Markdown'),markdown=ref(''),file=ref<File|null>(null),original=ref(''),uploadKey=ref(0)
  const settingStamp=ref<string|null>(null),settingEnabled=ref(false)
  const official=computed(()=>mode.value==='Official'),editor=computed(()=>mode.value==='Mine'||official.value)
  const now=useNow({interval:1000}),leaveOpen=ref(false),reviewOpen=ref(false),reviewAction=ref<Action>('Publish'),reason=ref('')
  const reviewTarget=ref<ReturnType<typeof postgameReviewTarget>>(null)
  const reviewCaption=computed(()=>reviewTarget.value?message('liveSolo.postgame.reviewTarget',{
    title:reviewTarget.value.title,author:reviewTarget.value.source==='Official'?message('liveSolo.postgame.official'):reviewTarget.value.authorName,
    version:reviewTarget.value.versionNumber}):null)
  let disposed=false,request=0,bodyRequest=0,allowLeave=false,leaveTo:string|null=null,initialized=false
  const previewLease=new PostgamePreviewLease({
    changed:url=>{pdf.value=url},
    failed:cause=>{if(!disposed)error.value=parseApiError(cause,message('challengeWriteUp.loadFailed')).displayMessage},
  })
  const currentQuestion=computed(()=>postgameQuestion(questions.value,questionId.value,route.params.roundId))
  const questionOptions=computed(()=>questions.value.filter(q=>q.id).map(q=>({value:q.id!,label:(q.replay??0)>0
    ?t('liveSolo.postgame.replayedQuestion',{round:q.roundNumber??1,replay:q.replay??0,title:q.title??'—'})
    :t('liveSolo.postgame.roundQuestion',{round:q.roundNumber??1,title:q.title??'—'})})))
  const root=computed(()=>editor.value ? listing.value?.items?.find(row=>official.value?row.source==='Official':row.teamId===listing.value?.access?.teamId)??null
    :listing.value?.items?.find(row=>row.id===selected.value)??null)
  const version=computed(()=>editor.value||mode.value==='Review'?latestWriteUpVersion(root.value):root.value?.published??null)
  const items=computed(()=>(listing.value?.items??[]).filter(row=>mode.value==='Public'?!!row.publishedVersionId:true).filter(row=>row.id)
    .map(row=>({value:row.id!,label:row.source==='Official'?row.challengeTitle??'—':row.authorName??'—',row})))
  const dirty=computed(()=>editor.value&&!loading.value&&original.value!==''&&(file.value!=null||JSON.stringify([format.value,markdown.value])!==original.value))
  const canSave=computed(()=>editor.value&&!loading.value&&listing.value?.access?.settings?.enabled===true&&(official.value?listing.value.access.canManage===true:
    listing.value.access.canSubmit===true&&!!listing.value.access.settings.deadlineAt&&Date.parse(listing.value.access.settings.deadlineAt)>=now.value.getTime()))
  const canSubmit=computed(()=>canSave.value&&!!root.value?.draft&&!dirty.value&&!busy.value)
  const reviewable=computed(()=>!!root.value&&!!version.value&&(listing.value?.access?.canJudge||listing.value?.access?.canManage))
  const stateKey=computed(()=>writeUpStatusKey(root.value))
  function path() { const q=currentQuestion.value!;return {competitionId:competitionId.value,matchId:matchId.value,roundId:q.roundId!,questionId:q.id!} }
  async function selectQuestion(id:string) {const q=questions.value.find(row=>row.id===id);if(q?.roundId)await router.push(`/competitions/${competitionId.value}/live-solo/postgame/${matchId.value}/${q.roundId}/${id}`)}
  async function read() {
    const sequence=++bodyRequest;content.value=null;previewLease.close()
    const v=version.value;if(!v?.id||!currentQuestion.value)return
    try {
      const target={...path(),versionId:v.id},isStaff=official.value||mode.value==='Review'
      const result=await getLiveSoloWriteUpContent({path:target,query:{staff:isStaff}})
      if(disposed||sequence!==bodyRequest)return
      if(result.error||!result.data)throw parseApiError(result.error,message('challengeWriteUp.loadFailed'))
      content.value=result.data
      if(editor.value) {format.value=result.data.format??'Markdown';markdown.value=result.data.markdown??'';file.value=null;original.value=JSON.stringify([format.value,markdown.value])}
      if(result.data.format==='Pdf') {
        await previewLease.open(async signal=>{
          const grant=await prepareLiveSoloWriteUpBrowserAccess({path:target,body:{staff:isStaff},signal})
          if(grant.error||!grant.data?.previewUrl)throw parseApiError(grant.error,message('challengeWriteUp.loadFailed'))
          return grant.data.previewUrl
        })
      }
    }catch(cause){if(!disposed&&sequence===bodyRequest)error.value=parseApiError(cause,message('challengeWriteUp.loadFailed')).displayMessage}
  }
  async function load() {
    if(disposed||busy.value)return
    const sequence=++request;bodyRequest++;previewLease.close();content.value=null;loading.value=true
    try {
      if(!context?.competition.value)await context?.refresh()
      if(!initialized){if(staff.value)mode.value='Review';initialized=true}
      if(manager.value){const policy=await getChallengeWriteUpSettings({path:{competitionId:competitionId.value}});if(policy.data){settingStamp.value=policy.data.concurrencyStamp??null;settingEnabled.value=policy.data.settings?.enabled??false}}
      const result=await listLiveSoloPostgameQuestions({path:{competitionId:competitionId.value,matchId:matchId.value}})
      if(disposed||sequence!==request)return
      if(result.error||!result.data)throw parseApiError(result.error,message('liveSolo.postgame.loadFailed'))
      questions.value=result.data.items??[]
      if(!questionId.value&&questionOptions.value[0]) {allowLeave=true;await selectQuestion(questionOptions.value[0].value);return}
      if(!currentQuestion.value) {listing.value=null;return}
      const listed=await listLiveSoloWriteUps({path:path(),query:{staff:official.value||mode.value==='Review'}})
      if(disposed||sequence!==request)return
      if(listed.error||!listed.data)throw parseApiError(listed.error,message('liveSolo.postgame.loadFailed'))
      listing.value=listed.data;error.value=null;selected.value=items.value.some(row=>row.value===selected.value)?selected.value:items.value[0]?.value??null
      if(editor.value&&!root.value){format.value='Markdown';markdown.value='';file.value=null;original.value=JSON.stringify([format.value,markdown.value])}
      else await read()
    }catch(cause){if(!disposed&&sequence===request)error.value=parseApiError(cause,message('liveSolo.postgame.loadFailed')).displayMessage}
    finally{if(sequence===request)loading.value=false}
  }
  async function tab(value:unknown) {
    if(!['Public','Mine','Official','Review'].includes(String(value))||busy.value||dirty.value)return
    mode.value=value as typeof mode.value;await load()
  }
  async function select(id:string) {if(!dirty.value&&!busy.value){selected.value=id;await read()}}
  function chooseFormat(value:unknown){if(canSave.value&&!busy.value&&(value==='Markdown'||value==='Pdf'))format.value=value}
  function chooseFile(event:Event){if(!canSave.value)return;const picked=(event.target as HTMLInputElement).files?.[0]??null;
    if(picked&&(!picked.name.toLowerCase().endsWith('.pdf')||picked.size>64*1024*1024)){error.value=message('challengeWriteUp.error.InvalidContent');return}file.value=picked}
  async function save() {
    if(!canSave.value||!currentQuestion.value||busy.value)return
    if(format.value==='Markdown'?!markdown.value.trim():!file.value){error.value=message('challengeWriteUp.bodyRequired');return}
    busy.value=true
    try {
      const common={official:official.value,expectedStamp:root.value?.concurrencyStamp??null}
      const result=format.value==='Markdown'?await saveLiveSoloWriteUpDraft({path:path(),body:{...common,markdown:markdown.value}})
        :await saveLiveSoloWriteUpPdfDraft({path:path(),body:{...common,file:file.value!}})
      if(result.error||!result.data)throw parseApiError(result.error,message('challengeWriteUp.saveFailed'))
      original.value=JSON.stringify([format.value,markdown.value]);file.value=null;uploadKey.value++;error.value=null
    }catch(cause){error.value=parseApiError(cause,message('challengeWriteUp.saveFailed')).displayMessage}
    finally{busy.value=false;if(!dirty.value)await load()}
  }
  async function submit(){if(!canSubmit.value||!root.value?.concurrencyStamp)return;busy.value=true;try{
    const result=await submitLiveSoloWriteUp({path:path(),body:{official:official.value,expectedStamp:root.value.concurrencyStamp}})
    if(result.error||!result.data)throw parseApiError(result.error,message('challengeWriteUp.saveFailed'));error.value=null
  }catch(cause){error.value=parseApiError(cause,message('challengeWriteUp.saveFailed')).displayMessage}finally{busy.value=false;const problem=error.value;await load();if(problem)error.value=problem}}
  function openReview(action:Action){if(!reviewable.value||!root.value?.id||!version.value?.id||!root.value.concurrencyStamp||dirty.value||busy.value)return;
    const target=postgameReviewTarget(root.value,action);if(!target)return;
    reviewTarget.value=target;reviewAction.value=action;reason.value='';reviewOpen.value=true}
  function setReview(value:boolean){if(!busy.value)reviewOpen.value=value}
  async function review(){const target=reviewTarget.value;if(!target||busy.value)return;busy.value=true;try{
    const result=await reviewLiveSoloWriteUp({path:{...path(),writeUpId:target.writeUpId},body:{versionId:target.versionId,expectedStamp:target.expectedStamp,action:reviewAction.value,reason:reason.value||null}})
    if(result.error||!result.data)throw parseApiError(result.error,message('challengeWriteUp.saveFailed'));reviewOpen.value=false;error.value=null
  }catch(cause){error.value=parseApiError(cause,message('challengeWriteUp.saveFailed')).displayMessage}finally{busy.value=false;if(!reviewOpen.value)await load()}}
  async function download(){if(!version.value?.id||busy.value)return;busy.value=true;try{
    const result=await prepareLiveSoloWriteUpBrowserAccess({path:{...path(),versionId:version.value.id},body:{staff:official.value||mode.value==='Review'}})
    if(result.error||!result.data?.downloadUrl)throw parseApiError(result.error,message('challengeWriteUp.loadFailed'));startLiveSoloBrowserDownload(result.data.downloadUrl)
  }catch(cause){error.value=parseApiError(cause,message('challengeWriteUp.loadFailed')).displayMessage}finally{busy.value=false}}
  async function reload(){if(!dirty.value&&!busy.value)await load()}
  async function enable(){if(!manager.value||!settingStamp.value||busy.value)return;busy.value=true;try{
    const result=await adminUpdateChallengeWriteUpSettings({path:{competitionId:competitionId.value},body:{expectedStamp:settingStamp.value,enabled:true}})
    if(result.error||!result.data)throw parseApiError(result.error,message('challengeWriteUp.saveFailed'));error.value=null
  }catch(cause){error.value=parseApiError(cause,message('challengeWriteUp.saveFailed')).displayMessage}finally{busy.value=false;const problem=error.value;await load();if(problem)error.value=problem}}
  function setLeave(value:boolean){if(!busy.value)leaveOpen.value=value}
  async function leave(){if(!busy.value&&leaveTo){allowLeave=true;leaveOpen.value=false;await router.push(leaveTo)}}
  async function back(){await router.push(`/competitions/${competitionId.value}/live-solo`)}
  function beforeUnload(event:BeforeUnloadEvent){if(dirty.value){event.preventDefault();event.returnValue=''}}
  onBeforeRouteLeave(to=>{if(allowLeave)return true;if(busy.value)return false;if(!dirty.value)return true;leaveTo=to.fullPath;leaveOpen.value=true;return false})
  onMounted(()=>{void load();window.addEventListener('beforeunload',beforeUnload)})
  onScopeDispose(()=>{disposed=true;request++;bodyRequest++;previewLease.close();window.removeEventListener('beforeunload',beforeUnload)})
  return {questionOptions,questionId,selectQuestion,mode,tab,staff,manager,loading,busy,error,listing,items,selected,select,root,version,stateKey,content,pdf,
    editor,official,format,chooseFormat,markdown,chooseFile,uploadKey,dirty,canSave,canSubmit,save,submit,reviewable,reviewOpen,setReview,reviewAction,reviewCaption,reason,openReview,review,
    download,reload,back,leaveOpen,setLeave,leave,settingEnabled,enable}
}
export type LiveSoloPostgameWriteUpsState=import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloPostgameWriteUps>>
