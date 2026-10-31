#include "bridge.h"
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <stdexcept>
#include <string>
#include <vector>
int main(int argc,char** argv) {
    void* h=nullptr;
    try {
        if(argc!=2)throw std::runtime_error("Expected resource directory");
        h=sl_create(argv[1]);if(!h)throw std::runtime_error(sl_error(nullptr));
        auto check=[&](bool ok,const char* message){if(!ok)throw std::runtime_error(std::string(message)+": "+sl_error(h));};
        check(std::string(sl_catalog(h)).find("kodak_portra_400")!=std::string::npos,"Film catalog");
        check(std::string(sl_catalog(h)).find("cinestill_800t")!=std::string::npos,"Derived film catalog");
        const int w=96,ht=64;
        std::vector<uint32_t> pixels(w*ht);
        for(int y=0;y<ht;++y)for(int x=0;x<w;++x)pixels[y*w+x]=0xff000000u|((x*255/(w-1))<<16)|((y*255/(ht-1))<<8)|80;
        const char* params=R"({"film_stock":"kodak_portra_400","print_stock":"kodak_portra_endura","scan_film":false,"grain_active":false,"halation_active":false,"glare_active":false,"print_exposure":1})";
        check(sl_open_argb(h,pixels.data(),w,ht,params),"Open encoded sRGB");
        check(sl_width(h)==w&&sl_height(h)==ht,"Dimensions");
        check(std::equal(pixels.begin(),pixels.end(),sl_pixels(h,1)),"Original orientation and channels");
        std::vector<uint32_t> before(sl_pixels(h,0),sl_pixels(h,0)+w*ht);
        check(before!=pixels,"Must use film model");
#ifdef _WIN32
        // Refuse a replacement without actually exhausting the machine.
        // Restore the diagnostic environment before checking/throwing.
        const char* prior = std::getenv("SPEKTRAFILM_TEST_COMMIT_RESERVE_GIB");
        const std::string saved = prior ? prior : "";
        _putenv_s("SPEKTRAFILM_TEST_COMMIT_RESERVE_GIB", "64");
        std::vector<uint32_t> replacement(128*96,0xffa06040u);
        const int accepted = sl_open_argb(h,replacement.data(),128,96,params);
        _putenv_s("SPEKTRAFILM_TEST_COMMIT_RESERVE_GIB", saved.c_str());
        check(!accepted,"Retain commit safety guard");
        check(sl_width(h)==w&&sl_height(h)==ht,"Failed replacement keeps dimensions");
        check(std::equal(before.begin(),before.end(),sl_pixels(h,0)),"Memory pressure keeps export pixels");
        check(sl_prepare_import(h,4096)>=256,"Recover after memory pressure");
        check(sl_open_argb(h,pixels.data(),w,ht,params),"Import retry after memory pressure");
        check(std::equal(before.begin(),before.end(),sl_pixels(h,0)),"Retry preserves deterministic output");
#endif
        check(!sl_render(h,R"({"film_stock":"not_a_stock"})"),"Reject invalid stock");
        check(std::equal(before.begin(),before.end(),sl_pixels(h,0)),"Retain image after bad edit");
        check(!sl_open_raw(h,"missing-file.nef",1024,params),"Reject invalid RAW");
        check(std::equal(before.begin(),before.end(),sl_pixels(h,0)),"Retain image after bad import");
        const char* brighter=R"({"film_stock":"kodak_portra_400","print_stock":"kodak_portra_endura","scan_film":false,"grain_active":false,"halation_active":false,"glare_active":false,"print_exposure":0.5})";
        check(sl_render(h,brighter),"Print brightness edit after failure");
        std::vector<uint32_t> after(sl_pixels(h,0),sl_pixels(h,0)+w*ht);
        check(before!=after,"Brightness must change pixels");
        check(sl_render(h,params),"Restore settings");
        check(std::equal(before.begin(),before.end(),sl_pixels(h,0)),"Deterministic restore");
        check(sl_render(h,R"({"film_stock":"fujifilm_provia_100f","print_stock":"kodak_portra_endura","scan_film":true,"grain_active":false,"halation_active":false,"glare_active":false,"print_exposure":1})"),"Slide film");
        sl_destroy(h);std::puts("PASS: real Vulkan, sRGB input, catalog, edit, rollback, RAW rejection, slide");return 0;
    }catch(const std::exception& e){std::fprintf(stderr,"FAIL: %s\n",e.what());sl_destroy(h);return 1;}
}
