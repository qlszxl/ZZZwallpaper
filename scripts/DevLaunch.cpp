// A development launcher for running an unpackaged WinUI app from a packaged IDE.
// Windows otherwise inherits the IDE's package identity and resolves ms-appx resources in that package.
#include <Windows.h>
#include <string>
#include <vector>
#include <iostream>
#include <appmodel.h>
int wmain(int argc,wchar_t** argv) {
    if(argc<2)return 2;
    SIZE_T size=0;InitializeProcThreadAttributeList(nullptr,1,0,&size);
    std::vector<unsigned char> memory(size);
    STARTUPINFOEXW startup{};startup.StartupInfo.cb=sizeof(startup);
    startup.lpAttributeList=reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(memory.data());
    if(!InitializeProcThreadAttributeList(startup.lpAttributeList,1,0,&size))return 3;
    DWORD policy=PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE;
    if(!UpdateProcThreadAttribute(startup.lpAttributeList,0,PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY,&policy,sizeof(policy),nullptr,nullptr))return 4;
    std::wstring command=L"\""+std::wstring(argv[1])+L"\"";
    for(int i=2;i<argc;++i) {command+=L" \"";command+=argv[i];command+=L"\"";}
    PROCESS_INFORMATION process{};
    bool debug=GetEnvironmentVariableW(L"LUMAWALL_STARTUP_DEBUG",nullptr,0)>0;
    BOOL ok=CreateProcessW(argv[1],command.data(),nullptr,nullptr,FALSE,EXTENDED_STARTUPINFO_PRESENT|(debug?DEBUG_ONLY_THIS_PROCESS:0),nullptr,nullptr,&startup.StartupInfo,&process);
    auto error=GetLastError();DeleteProcThreadAttributeList(startup.lpAttributeList);
    if(!ok) {std::cout<<"Launch error "<<error<<std::endl;return 5;}
    std::cout<<"Started PID "<<process.dwProcessId<<std::endl;CloseHandle(process.hThread);
    UINT32 packageLength=0;auto packageResult=GetPackageFullName(process.hProcess,&packageLength,nullptr);
    std::cout<<"Package identity query "<<packageResult<<std::endl;
    if(debug) {
        DEBUG_EVENT event{};
        while(WaitForDebugEvent(&event,INFINITE)) {
            DWORD status=DBG_CONTINUE;
            if(event.dwDebugEventCode==EXCEPTION_DEBUG_EVENT) {
                auto& ex=event.u.Exception.ExceptionRecord;
                if(ex.ExceptionCode!=EXCEPTION_BREAKPOINT) {
                    std::cout<<"Exception "<<std::hex<<ex.ExceptionCode<<" first="<<event.u.Exception.dwFirstChance<<std::dec<<std::endl;
                    if(ex.ExceptionCode==0x40080201&&ex.NumberParameters>=3) {
                        std::vector<wchar_t> text(2048);SIZE_T read;
                        ReadProcessMemory(process.hProcess,(void*)ex.ExceptionInformation[2],text.data(),4094,&read);
                        std::wcout<<L"WinRT error "<<std::hex<<ex.ExceptionInformation[0]<<L" "<<text.data()<<std::dec<<std::endl;
                    }
                    if(ex.ExceptionCode==0xC000027B&&ex.NumberParameters>=2) {
                        for(ULONG_PTR i=0;i<ex.ExceptionInformation[1]&&i<8;++i) {
                            ULONG_PTR record=0;HRESULT hr=0;SIZE_T read;
                            ReadProcessMemory(process.hProcess,(void*)(ex.ExceptionInformation[0]+i*sizeof(void*)),&record,sizeof(record),&read);
                            ReadProcessMemory(process.hProcess,(void*)(record+8),&hr,sizeof(hr),&read);
                            std::cout<<"Stowed HRESULT "<<std::hex<<hr<<std::dec<<std::endl;
                        }
                    }
                    status=DBG_EXCEPTION_NOT_HANDLED;
                }
            }
            if(event.dwDebugEventCode==CREATE_PROCESS_DEBUG_EVENT&&event.u.CreateProcessInfo.hFile)CloseHandle(event.u.CreateProcessInfo.hFile);
            if(event.dwDebugEventCode==LOAD_DLL_DEBUG_EVENT&&event.u.LoadDll.hFile)CloseHandle(event.u.LoadDll.hFile);
            if(event.dwDebugEventCode==OUTPUT_DEBUG_STRING_EVENT) {
                auto& s=event.u.DebugString;SIZE_T read;
                if(s.fUnicode) {std::vector<wchar_t> text(s.nDebugStringLength+1);ReadProcessMemory(process.hProcess,s.lpDebugStringData,text.data(),s.nDebugStringLength*2,&read);std::wcout<<text.data()<<std::endl;}
                else {std::vector<char> text(s.nDebugStringLength+1);ReadProcessMemory(process.hProcess,s.lpDebugStringData,text.data(),s.nDebugStringLength,&read);std::cout<<text.data()<<std::endl;}
            }
            ContinueDebugEvent(event.dwProcessId,event.dwThreadId,status);
            if(event.dwDebugEventCode==EXIT_PROCESS_DEBUG_EVENT)break;
        }
    }
    WaitForSingleObject(process.hProcess,INFINITE);DWORD exitCode=0;GetExitCodeProcess(process.hProcess,&exitCode);
    std::cout<<"Exit code "<<exitCode<<std::endl;CloseHandle(process.hProcess);return (int)exitCode;
}
