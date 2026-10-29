export const maximumProjectBytes=128_000_000;

export async function readProjectFile(file,signal){
 signal?.throwIfAborted();
 if(file.size>maximumProjectBytes)throw Error('The project is larger than 128 MB.');
 const text=await file.text();
 signal?.throwIfAborted();
 return JSON.parse(text);
}
