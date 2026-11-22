(() => {
  'use strict';
  const model=window.INSTALLER_PREVIEW;
  if (!model?.previewOnly) throw new Error('This page only supports installation previews.');
  const $=id=>document.getElementById(id), messages=model.messages;
  const state={page:'settings',scenario:'normal',progress:0,timer:null,returnFocus:null};
  let toastTimer;
  const scenarios={
    normal:'可修改安装位置和快捷方式，再点击“开始安装”体验完整流程。',
    upgrade:'模拟检测到旧开发版：保留当前设置，主按钮改为“开始更新”。',
    running:'模拟软件仍在运行：开始时暂停流程，提示从托盘退出后重试。',
    space:'模拟可用空间不足：阻止开始，返回设置更换安装位置。',
    permission:'模拟安装目录无写入权限：提示选择可写目录，不自动请求管理员权限。',
    newer:'模拟目标目录已有更高版本：阻止降级，不覆盖现有程序。',
    failure:'模拟写入至 52% 时失败：显示原因与返回设置入口，不假报安装成功。'
  };
  const errors={
    running:['请先退出正在运行的软件','ZZZwallpaper 仍在运行','从托盘菜单退出软件，再返回这里继续。此预览不会关闭真实程序。'],
    space:['安装位置的可用空间不足','需要更多可用空间','释放磁盘空间或选择其他位置后重试。此处为模拟状态，不读取实际磁盘空间。'],
    permission:['无法写入所选安装位置','当前用户没有写入权限','选择当前用户可写的文件夹，例如默认安装位置。'],
    newer:['所选目录已有更高版本','已阻止降级安装','请更换安装目录，或使用较新的安装包。'],
    failure:['写入程序文件时发生错误','程序文件无法写入','请检查目标目录的权限和文件占用后重试。此预览没有写入文件，也不模拟恢复已完成。']
  };
  document.querySelectorAll('[data-message]').forEach(el=>{el.textContent=messages[el.dataset.message]||el.dataset.message;});
  $('brand-version').textContent=`v${model.version} · ${model.channel}`;
  $('footer-version').textContent=`ZZZwallpaper ${model.version} · ${model.channel}`;
  $('install-path').value=model.defaultDirectory;
  $('size-label').textContent=model.payloadMiB?`程序文件约 ${Math.ceil(model.payloadMiB)} MB`:'所需空间以安装检查为准';
  function stop(){if(state.timer!==null)clearInterval(state.timer);state.timer=null;}
  function announce(text){clearTimeout(toastTimer);$('toast').textContent=text;$('toast').hidden=false;toastTimer=setTimeout(()=>$('toast').hidden=true,4200);}
  function destinations(){document.querySelectorAll('.destination').forEach(el=>el.textContent=$('install-path').value);}
  function show(page,focus=true){
    state.page=page;
    document.querySelectorAll('.page').forEach(el=>el.hidden=el.id!==`page-${page}`);
    const active=page==='complete'?2:page==='progress'?1:0;
    document.querySelectorAll('.steps li').forEach((el,i)=>{el.classList.toggle('done',i<active);if(i===active)el.setAttribute('aria-current','step');else el.removeAttribute('aria-current');});
    destinations();
    if(focus){const heading=$(`page-${page}`).querySelector('h2');heading.tabIndex=-1;heading.focus({preventScroll:true});}
  }
  function syncScenario(){
    $('upgrade-notice').hidden=state.scenario!=='upgrade';
    $('start-install').firstElementChild.textContent=state.scenario==='upgrade'?messages.StartUpdate:messages.StartInstall;
    $('scenario-description').textContent=scenarios[state.scenario];
  }
  function validPath(value){
    if(!/^[a-z]:\\/i.test(value)||/[<>"|?*\x00-\x1f]/.test(value)||value.slice(2).includes(':')||value.trim().length<4) return '请输入完整的本地文件夹路径，例如 D:\\Apps\\ZZZwallpaper。';
    const segments=value.slice(3).split('\\');
    if(!segments.every(s=>s.length>0&&s!=='.'&&s!=='..'&&!/[. ]$/.test(s)&&! /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(s))) return '请使用有效的文件夹名称，不要选择磁盘根目录。';
    return '';
  }
  function setProgress(value){
    state.progress=value;$('percent').textContent=value;$('progressbar').setAttribute('aria-valuenow',String(value));$('progress-fill').style.width=`${value}%`;
    const phase=value<10?0:value<92?1:2;
    $('progress-status').textContent=[messages.PreparingFiles,messages.CopyingFiles,messages.CreatingShortcuts][phase];
    document.querySelectorAll('[data-phase]').forEach((el,i)=>{el.classList.toggle('active',i===phase);el.classList.toggle('done',i<phase);el.lastElementChild.textContent=i<phase?'已完成':i===phase?'进行中':'等待';});
  }
  function error(kind){stop();const [description,reason,next]=errors[kind];$('error-description').textContent=description;$('error-reason').textContent=reason;$('error-next').textContent=next;show('error');}
  function advance(){
    if(document.querySelector('dialog[open]')||state.page!=='progress')return;
    const next=Math.min(100,state.progress+2);setProgress(next);
    if(state.scenario==='failure'&&next>=52){error('failure');return;}
    if(next>=100){stop();$('shortcut-result').textContent=$('desktop-shortcut').checked?'创建':'不创建';$('launch-after').checked=false;show('complete');}
  }
  function reset(keepScenario=true){
    stop();state.returnFocus=null;document.querySelectorAll('dialog[open]').forEach(d=>d.close());
    clearTimeout(toastTimer);$('toast').hidden=true;
    document.querySelector('.installer').hidden=false;$('closed-preview').hidden=true;
    $('path-error').hidden=true;$('install-path').removeAttribute('aria-invalid');$('launch-after').checked=false;
    if(!keepScenario){state.scenario='normal';$('scenario').value='normal';$('install-path').value=model.defaultDirectory;$('desktop-shortcut').checked=true;}
    setProgress(0);syncScenario();show('settings',false);
  }
  function closePreview(){stop();document.querySelector('.installer').hidden=true;$('closed-preview').hidden=false;$('reopen-preview').focus();}
  function modal(id){state.returnFocus=document.activeElement;$(id).showModal();if(id==='cancel-dialog')$('keep-installing').focus();}
  document.querySelectorAll('dialog').forEach(d=>d.addEventListener('close',()=>{if(state.returnFocus?.isConnected&&!state.returnFocus.closest('[hidden]'))state.returnFocus.focus();}));
  $('start-install').addEventListener('click',()=>{
    const input=$('install-path');input.value=input.value.trim().replace(/\\+$/,'');const issue=validPath(input.value);
    $('path-error').hidden=!issue;$('path-error').textContent=issue;
    if(issue){input.setAttribute('aria-invalid','true');input.focus();return;}input.removeAttribute('aria-invalid');
    if(errors[state.scenario]&&state.scenario!=='failure'){error(state.scenario);return;}
    stop();setProgress(0);show('progress');state.timer=setInterval(advance,$('slow-preview').checked?550:160);
  });
  $('cancel-progress').addEventListener('click',()=>modal('cancel-dialog'));
  $('keep-installing').addEventListener('click',()=>$('cancel-dialog').close());
  $('confirm-cancel').addEventListener('click',()=>{stop();state.returnFocus=null;$('cancel-dialog').close();show('cancelled');});
  $('close-window').addEventListener('click',()=>{if(state.page==='progress')modal('cancel-dialog');else closePreview();});
  $('finish-preview').addEventListener('click',()=>{closePreview();if($('launch-after').checked)announce('已预览启动选项；没有启动真实软件。');});
  $('error-back').addEventListener('click',()=>show('settings'));
  $('cancelled-back').addEventListener('click',()=>show('settings'));
  $('reopen-preview').addEventListener('click',()=>reset());
  $('reset-preview').addEventListener('click',()=>reset(false));
  $('scenario').addEventListener('change',e=>{state.scenario=e.target.value;reset();});
  $('slow-preview').addEventListener('change',()=>{if(state.timer!==null){stop();state.timer=setInterval(advance,$('slow-preview').checked?550:160);}});
  $('browse-path').addEventListener('click',()=>{$('new-path').value=$('install-path').value;$('dialog-path-error').hidden=true;modal('path-dialog');$('new-path').focus();});
  $('default-path').addEventListener('click',()=>$('new-path').value=model.defaultDirectory);
  $('other-path').addEventListener('click',()=>$('new-path').value='D:\\Apps\\ZZZwallpaper');
  $('discard-path').addEventListener('click',()=>$('path-dialog').close());
  $('save-path').addEventListener('click',()=>{const value=$('new-path').value.trim().replace(/\\+$/,'');const issue=validPath(value);$('dialog-path-error').hidden=!issue;$('dialog-path-error').textContent=issue;if(issue){$('new-path').focus();return;}$('install-path').value=value;$('install-path').removeAttribute('aria-invalid');$('path-error').hidden=true;$('path-dialog').close();});
  $('new-path').addEventListener('keydown',e=>{if(e.key==='Enter'){$('save-path').click();e.preventDefault();}});
  $('licenses').addEventListener('click',()=>modal('license-dialog'));
  $('close-licenses').addEventListener('click',()=>$('license-dialog').close());
  document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!document.querySelector('dialog[open]')&&state.page==='progress'){modal('cancel-dialog');e.preventDefault();}});
  window.addEventListener('pagehide',stop);
  reset();
})();
