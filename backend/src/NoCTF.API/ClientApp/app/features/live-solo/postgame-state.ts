import type { NoCtfApplicationLiveSoloResourcesLiveSoloPostgameQuestion as Question,NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpResponse as WriteUp,
  NoCtfDomainChallengesWriteUpsWriteUpReviewAction as Action } from '../../api'
import { latestWriteUpVersion } from '../writeups/writeup-state'
export function postgameQuestion(questions:Question[],questionId:string|null,roundId:unknown):Question|null {
  return questions.find(question=>question.id===questionId&&question.roundId===roundId)??null
}
export function postgameReviewVersion(root:WriteUp|null,action:Action):string|null {
  return action==='Withdraw'?root?.published?.id??null:latestWriteUpVersion(root)?.id??null
}
