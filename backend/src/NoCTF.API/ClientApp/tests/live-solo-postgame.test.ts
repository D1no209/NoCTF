import {describe,expect,test} from 'bun:test'
import {postgameQuestion,postgameReviewVersion,postgameReviewTarget} from '../app/features/live-solo/postgame-state'
describe('LiveSolo post-event scopes',()=>{
  test('question lookup requires the exact historical round and never guesses another scope',()=>{
    const questions=[{id:'question',roundId:'round',title:'Question'}]
    expect(postgameQuestion(questions,'question','round')?.title).toBe('Question')
    expect(postgameQuestion(questions,'question','another')).toBeNull();expect(postgameQuestion(questions,'missing','round')).toBeNull()
  })
  test('withdrawal targets the public version even when a newer submission exists',()=>{
    const root={published:{id:'public',state:'Approved' as const},submitted:{id:'pending',state:'Submitted' as const}}
    expect(postgameReviewVersion(root,'Withdraw')).toBe('public');expect(postgameReviewVersion(root,'Publish')).toBe('pending')
    expect(postgameReviewVersion(null,'Withdraw')).toBeNull()
  })
  test('review confirmation freezes the actual targeted version and official source metadata',()=>{
    const root={id:'root',concurrencyStamp:'stamp',challengeTitle:'Question',source:'Official' as const,authorName:'',
      published:{id:'public',number:1,state:'Approved' as const},submitted:{id:'pending',number:3,state:'Submitted' as const}}
    const withdrawn=postgameReviewTarget(root,'Withdraw')!
    expect(withdrawn.versionId).toBe('public');expect(withdrawn.versionNumber).toBe(1);expect(withdrawn.source).toBe('Official')
    root.challengeTitle='Another';expect(withdrawn.title).toBe('Question');expect(withdrawn.expectedStamp).toBe('stamp')
    expect(postgameReviewTarget(root,'Publish')?.versionNumber).toBe(3);expect(postgameReviewTarget(null,'Publish')).toBeNull()
  })
})
