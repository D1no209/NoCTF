import type { NoCtfApplicationLiveSoloResourcesLiveSoloPostgameQuestion as Question,NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpResponse as WriteUp,
  NoCtfDomainChallengesWriteUpsWriteUpReviewAction as Action } from '../../api'
import { latestWriteUpVersion } from '../writeups/writeup-state'
export function postgameQuestion(questions:Question[],questionId:string|null,roundId:unknown):Question|null {
  return questions.find(question=>question.id===questionId&&question.roundId===roundId)??null
}
export function postgameReviewVersion(root:WriteUp|null,action:Action):string|null {
  return action==='Withdraw'?root?.published?.id??null:latestWriteUpVersion(root)?.id??null
}
export function postgameReviewTarget(root:WriteUp|null,action:Action) {
  const version=action==='Withdraw'?root?.published:latestWriteUpVersion(root)
  if(!root?.id||!root.concurrencyStamp||!version?.id)return null
  return {writeUpId:root.id,versionId:version.id,expectedStamp:root.concurrencyStamp,title:root.challengeTitle??'—',
    source:root.source,authorName:root.authorName??'—',versionNumber:version.number??1}
}
