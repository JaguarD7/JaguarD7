let mode="image-to-video";
const $=s=>document.querySelector(s);
const tabs=[...document.querySelectorAll(".tab")];
const sourceTitle=$("#sourceTitle"),sourceHint=$("#sourceHint"),prompt=$("#prompt");
const configs={
 "image-to-video":["ارفع صورتك","هذه هي نقطة البداية للفيديو. استخدم صورة واضحة.","مثال: أمشي في شارع طوكيو وقت المطر، إضاءة سينمائية، الكاميرا تتبعني من الأمام..."],
 "face-swap":["ارفع الفيديو الأصلي","سيتم كشف الأشخاص وتجميع ظهور كل شخص عبر اللقطات.","اختياري: اكتب تعليمات مثل الحفاظ على الإضاءة الأصلية وتقليل تغيير ملامح الوجه."],
 "talking-video":["ارفع صورة أو صوت","يمكن استخدام صورة مع نص، أو صوت لتحريك الشفاه.","اكتب النص الذي تريد أن تقوله الشخصية أو وصف أسلوب الأداء."]
};
tabs.forEach(t=>t.onclick=()=>{tabs.forEach(x=>x.classList.remove("active"));t.classList.add("active");mode=t.dataset.mode;let c=configs[mode];sourceTitle.textContent=c[0];sourceHint.textContent=c[1];prompt.placeholder=c[2]});
$("#duration").oninput=e=>$("#durLabel").textContent=e.target.value+" ث";
$("#identity").oninput=e=>$("#idLabel").textContent=e.target.value+"%";
$("#source").onchange=e=>$("#sourceName").textContent=e.target.files[0]?.name||"";
$("#reference").onchange=e=>$("#refName").textContent=e.target.files[0]?.name||"";
document.querySelectorAll(".chips button").forEach(b=>b.onclick=()=>{prompt.value=(prompt.value?prompt.value+"، ":"")+b.dataset.prompt});
async function check(){
 try{let r=await fetch("/health");let j=await r.json();let s=$("#status");if(j.provider_configured){s.textContent="المحرك متصل";s.className="status ok"}else{s.textContent="الموقع جاهز · المحرك يحتاج ربط";s.className="status warn"}}catch(e){$("#status").textContent="تعذر فحص الخدمة"}
}
check();
$("#generate").onclick=async()=>{
 const f=new FormData();
 f.append("mode",mode);f.append("prompt",prompt.value);f.append("duration",$("#duration").value);f.append("identity_strength",$("#identity").value);
 if($("#source").files[0])f.append("source",$("#source").files[0]);
 if($("#reference").files[0])f.append("reference",$("#reference").files[0]);
 $("#notice").textContent="جاري إنشاء المشروع…";
 try{
  let r=await fetch("/api/jobs",{method:"POST",body:f});let j=await r.json();
  if(!r.ok)throw new Error(j.detail||"تعذر إنشاء المشروع");
  $("#notice").textContent=j.status==="provider_required"
   ?"تم رفع المشروع بنجاح. واجهة الموقع تعمل، لكن محرك التوليد السحابي لم يُربط بعد."
   :"تمت إضافة المهمة للطابور: "+j.id;
 }catch(e){$("#notice").textContent=e.message}
};