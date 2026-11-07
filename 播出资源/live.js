"use strict";
let engine, socket, session, seq=0, conn, acknowledged=false, closing=false, mediaTimer;
let stopRequested=false;
let delivery=Promise.resolve();
function report(type){window.chrome?.webview?.postMessage(type);}
function signal(type,payload){socket.send(JSON.stringify({type,live_id:session.live.id,conn_id:conn,seq_id:++seq,payload}));}
async function end(){
  if(closing)return; closing=true; clearInterval(mediaTimer);
  if(socket?.readyState===WebSocket.OPEN){try{if(acknowledged)signal(5,{hangup:{hangup_reason:"client_hangup"}});}finally{socket.close(1000);}}
  else if(socket)socket.close();
  if(engine){try{await engine.leaveChannel();}finally{await engine.destroy();engine=null;}}
}
async function begin(s,exitOnly){
  stopRequested=Boolean(exitOnly); session=s; conn=crypto.randomUUID(); closing=false;seq=0;acknowledged=false;
  try{
    const url=new URL("wss://"+s.host+"/live/v1/external-lives/"+s.live.id+"/stream");
    url.searchParams.set("conn_id",conn);url.searchParams.set("client_secret",s.client_secret);
    socket=new WebSocket(url);
    await new Promise((ok,fail)=>{
      const timer=setTimeout(()=>fail(Error("信令初始化超时")),12000);
      socket.onopen=()=>signal(1,{conn_init:{version:1}});
      socket.onmessage=e=>{const m=JSON.parse(e.data);if(m.type===2){clearTimeout(timer);acknowledged=!!m.payload?.conn_init_ack?.success;acknowledged?ok():fail(Error("信令认证失败"));}else if(m.type===6){report("平台已结束会话");end();}};
      socket.onerror=()=>{clearTimeout(timer);fail(Error("信令连接失败"));};
      socket.onclose=()=>{clearTimeout(timer);if(!closing){report("信令断开");end();}fail(Error("信令断开"));};
    });
    if(stopRequested||closing){await end();return;}
    engine=AliRtcEngine.getInstance();await engine.publishLocalAudioStream(false);await engine.publishLocalVideoStream(false);
    engine.setDefaultSubscribeAllRemoteAudioStreams(true);engine.setDefaultSubscribeAllRemoteVideoStreams(true);
    await engine.joinChannel(s.managed_rtc.token,s.managed_rtc.user_id);
    if(closing)return;
    const video=document.createElement("video");video.autoplay=true;video.playsInline=true;
    document.querySelector("#screen").replaceChildren(video);
    const show=()=>{const u=engine.remoteUsers?.[0];if(u)engine.setRemoteViewConfig(video,typeof u==="string"?u:u.userId,1);};
    show(); let ready=false;
    mediaTimer=setInterval(()=>{if(video.videoWidth>0&&video.currentTime>0){if(!ready){report("画面已到达");ready=true;}}else{show();}},500);
    report("RTC 已加入");return true;
  }catch(e){await end();report("连接失败："+e.message);throw e;}
}
function speak(encoded){
  const send=async()=>{
    if(!acknowledged||closing)throw Error("会话未连接");
    const bytes=Uint8Array.from(atob(encoded),c=>c.charCodeAt(0));
    let target=performance.now();
    for(let i=0;i<bytes.length;i+=960){if(closing||socket.readyState!==WebSocket.OPEN)throw Error("声音连接已结束");
      socket.send(bytes.subarray(i,i+960));target+=20;await new Promise(r=>setTimeout(r,Math.max(0,target-performance.now())));}
  };
  delivery=delivery.catch(()=>{}).then(send);return delivery;
}
if(window.chrome?.webview)window.chrome.webview.addEventListener("message",async e=>{
  try{if(e.data.command==="start")await begin(e.data.session,e.data.exitOnly);
      if(e.data.command==="transition"){
        const video=document.querySelector("video");
        if(video&&video.videoWidth)video.animate([{transform:"scale(1)"},{transform:"scale(1.045)"},{transform:"scale(1)"}],{duration:2200,easing:"ease-in-out"});
      }
      if(e.data.command==="motion"&&acknowledged&&!closing){signal(9,{text_msg:{msg_id:crypto.randomUUID(),content:e.data.prompt,timestamp:Date.now()}});}
      if(e.data.command==="speak"){await speak(e.data.audio);report("声音已送入");}
      if(e.data.command==="stop"){stopRequested=true;if(!socket||acknowledged||socket.readyState>=WebSocket.CLOSING)await end();}
  }catch(err){report("失败："+err.message);}
});
