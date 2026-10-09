import { expect, test } from 'bun:test'
import { startLiveSoloBrowserDownload } from '../app/utils/download'

test('LiveSolo recording and PDF downloads accept only exact same-origin routes with bounded presentation switches',()=>{
  const clicked: string[]=[]
  const originals=['window','document','showSaveFilePicker'].map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)] as const)
  Object.defineProperty(globalThis,'window',{configurable:true,value:{location:{origin:'https://noctf.test'}}})
  Object.defineProperty(globalThis,'document',{configurable:true,value:{body:{append:()=>{}},createElement:()=>({href:'',download:'',hidden:false,click(){clicked.push(this.href)},remove(){}})}})
  Object.defineProperty(globalThis,'showSaveFilePicker',{configurable:true,value:()=>{throw new Error('No system picker')}})
  const id='00000000-0000-0000-0000-000000000001'
  const match=`/api/v1/competitions/${id}/live-solo/matches/${id}`
  const recording=`${match}/recordings/${id}/file?download=true`
  const pdf=`${match}/rounds/${id}/questions/${id}/writeups/versions/${id}/file?staff=true&download=true`
  try {
    startLiveSoloBrowserDownload(recording);startLiveSoloBrowserDownload(pdf)
    expect(clicked).toEqual(['https://noctf.test'+recording,'https://noctf.test'+pdf])
    for(const unsafe of ['https://evil.test'+pdf,pdf+'&access_token=secret',pdf+'&staff=false',pdf.replace('staff=true','staff=1'),pdf+'#fragment',match+'/media/token',pdf.replace('/questions/','/attachments/')])
      expect(()=>startLiveSoloBrowserDownload(unsafe)).toThrow()
    expect(clicked).toHaveLength(2)
  }finally{for(const [key,original] of originals){if(original)Object.defineProperty(globalThis,key,original);else Reflect.deleteProperty(globalThis,key)}}
})
