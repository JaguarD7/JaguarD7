let mode="image-to-video";
const $=s=>document.querySelector(s);
const $$=s=>[...document.querySelectorAll(s)];
const tabs=$$(".tab");
const prompt=$("#prompt");
const sourceTitle=$("#sourceTitle");
const sourceHint=$("#sourceHint");

const configs={
  "image-to-video":{
    title:"Upload your image",
    hint:"Use a clear portrait for stronger identity consistency.",
    placeholder:"Example: I walk through a rainy Tokyo street at night, cinematic lighting, slow forward camera movement, natural body motion, realistic skin detail..."
  },
  "face-swap":{
    title:"Upload your source video",
    hint:"Use footage with a clearly visible target subject for stronger tracking.",
    placeholder:"Optional notes: preserve original lighting, keep natural expressions, maintain original audio..."
  },
  "talking-video":{
    title:"Upload a portrait or audio",
    hint:"Use a clean portrait and clear speech for better lip-sync.",
    placeholder:"Write the spoken text or describe the performance style..."
  }
};

tabs.forEach(t=>t.addEventListener("click",()=>{
  tabs.forEach(x=>x.classList.remove("active"));
  t.classList.add("active");
  mode=t.dataset.mode;
  const c=configs[mode];
  sourceTitle.textContent=c.title;
  sourceHint.textContent=c.hint;
  prompt.placeholder=c.placeholder;
}));

$("#duration").addEventListener("input",e=>$("#durLabel").textContent=e.target.value+"s");
$("#identity").addEventListener("input",e=>$("#idLabel").textContent=e.target.value+"%");
prompt.addEventListener("input",()=>$("#charCount").textContent=prompt.value.length+" characters");

$("#source").addEventListener("change",e=>{
  const f=e.target.files[0];
  $("#sourceName").textContent=f ? f.name : "";
});
$("#reference").addEventListener("change",e=>{
  const f=e.target.files[0];
  $("#refName").textContent=f ? f.name : "";
});

$$(".presets button").forEach(b=>b.addEventListener("click",()=>{
  prompt.value=(prompt.value ? prompt.value+", " : "")+b.dataset.prompt;
  $("#charCount").textContent=prompt.value.length+" characters";
}));

async function checkEngine(){
  const box=$("#status");
  try{
    const r=await fetch("/health",{cache:"no-store"});
    const j=await r.json();
    box.className="engine ok";
    box.querySelector("span").textContent=j.engine||"Wan 2.2 Free Engine";
  }catch{
    box.className="engine warn";
    box.querySelector("span").textContent="Engine reconnecting";
  }
}
checkEngine();

function renderOutput(url){
  const preview=$("#preview");
  if(!preview)return;
  preview.innerHTML='<video controls autoplay loop playsinline style="width:100%;height:100%;object-fit:contain;border-radius:18px;background:#050506"><source src="'+url+'" type="video/mp4"></video>';
}

async function watchJob(id){
  const notice=$("#notice");
  for(let i=0;i<240;i++){
    await new Promise(r=>setTimeout(r,3000));
    const r=await fetch("/api/jobs/"+id,{cache:"no-store"});
    const j=await r.json();
    if(!r.ok)throw new Error(j.detail||"Could not read render status");
    const labels={starting:"Preparing image",connecting:"Connecting to Wan 2.2",queued:"ZeroGPU queue / rendering",completed:"Render complete",failed:"Render failed",waiting_capacity:"Waiting for free GPU capacity"};
    notice.textContent=(labels[j.status]||j.status)+" · "+(j.progress||0)+"%";
    if(j.directed_prompt && j.status==="queued") notice.title="ONYX Director: "+j.directed_prompt;
    if(j.status==="completed"){
      renderOutput(j.output_url);
      return;
    }
    if(j.status==="waiting_capacity"){
      notice.textContent="Free GPU capacity is exhausted for now. Your setup is working; free compute needs to reset.";
      return;
    }
    if(j.status==="failed")throw new Error(j.error||"Free engine failed. Please retry.");
  }
  throw new Error("The free GPU queue is taking too long. Retry in a moment.");
}

$("#generate").addEventListener("click",async()=>{
  const f=new FormData();
  f.append("mode",mode);
  f.append("prompt",prompt.value);
  f.append("duration",Math.min(Number($("#duration").value),5));
  f.append("identity_strength",$("#identity").value);
  if($("#source").files[0])f.append("source",$("#source").files[0]);
  if($("#reference").files[0])f.append("reference",$("#reference").files[0]);

  const notice=$("#notice");
  if(mode==="image-to-video"&&!$("#source").files[0]){
    notice.textContent="Upload an image first.";
    return;
  }
  notice.textContent="Submitting to Wan 2.2 Free Engine...";
  try{
    const r=await fetch("/api/jobs",{method:"POST",body:f});
    const j=await r.json();
    if(!r.ok)throw new Error(j.detail||"Could not start render");
    notice.textContent="Render started · "+j.id;
    await watchJob(j.id);
  }catch(e){
    notice.textContent="Error · "+e.message;
  }
});

const observer=new IntersectionObserver(entries=>{
  entries.forEach(e=>{
    if(e.isIntersecting){
      e.target.classList.add("show");
      observer.unobserve(e.target);
    }
  });
},{threshold:.12});
$$(".reveal").forEach(el=>observer.observe(el));

$$(".demo-window button").forEach(btn=>btn.addEventListener("click",()=>{
  const card=btn.closest(".demo-window");
  card.animate(
    [
      {transform:"scale(1)"},
      {transform:"scale(.985)"},
      {transform:"scale(1)"}
    ],
    {duration:420,easing:"ease-out"}
  );
}));

window.addEventListener("mousemove",e=>{
  if(window.innerWidth<900)return;
  const stage=$(".stage");
  if(!stage)return;
  const x=(e.clientX/window.innerWidth-.5)*8;
  const y=(e.clientY/window.innerHeight-.5)*6;
  const monitor=stage.querySelector(".monitor");
  if(monitor) monitor.style.transform="perspective(900px) rotateY("+(-5+x*.35)+"deg) rotateX("+(2-y*.2)+"deg)";
});