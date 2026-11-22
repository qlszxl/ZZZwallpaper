#include "AudioCapture.h"
#include <atomic>
#include <iostream>
#include <iomanip>
#include <locale>
#include <thread>
#include <chrono>
#include <string>

int main() {
    std::atomic_bool stop=false;
    AudioCapture capture(true);
    // Parent owns stdin; EOF releases capture even after an unexpected parent exit.
    std::thread input([&] {std::string line;while(std::getline(std::cin,line))if(line=="stop")break;stop=true;});
    capture.SetEnabled(true);std::cout.imbue(std::locale::classic());std::cout<<std::fixed<<std::setprecision(6);
    while(!stop&&std::cout) {
        auto snapshot=capture.SnapshotFrame();const auto& levels=snapshot.levels;const auto& bands=snapshot.bands;
        std::cout<<"{\"available\":"<<(levels.available?"true":"false")<<",\"bands\":[";
        for(size_t i=0;i<bands.size();i++) {if(i)std::cout<<',';std::cout<<(levels.available?bands[i]:0);}
        std::cout<<"]}"<<std::endl;std::this_thread::sleep_for(std::chrono::milliseconds(33));
    }
    capture.SetEnabled(false);
    // The parent closes stdin or sends stop during normal shutdown.
    if(input.joinable())input.join();return 0;
}
