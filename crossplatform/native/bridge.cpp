#include "bridge.h"
#include "spektrafilm/spk_engine.h"
#include "io/raw_decoder.hpp"
#include "json.hpp"
#include "profile.hpp"
#include <algorithm>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <memory>
#include <stdexcept>
#include <vector>

namespace {
using spk::Json;
using Engine = std::unique_ptr<spk_engine, decltype(&spk_engine_destroy)>;
using Session = std::unique_ptr<spk_session, decltype(&spk_session_release)>;
thread_local std::string create_error;
struct State {
    Engine engine{nullptr, spk_engine_destroy};
    Session session{nullptr, spk_session_release};
    std::string error, catalog;
    Json settings;
    std::vector<uint32_t> pixels, original;
    int width = 0, height = 0;
    double elapsed = 0;
};
void require(bool ok, const std::string& error) { if (!ok) throw std::runtime_error(error); }
Json parse(const char* text) {
    Json j; std::string error;
    require(text && Json::parse(text, j, error) && j.is_object(), "Invalid settings: " + error);
    return j;
}
Json parameters(const char* text) {
    Json j = parse(text);
    static const char* allowed[] = {"film_stock", "print_stock", "scan_film", "exposure_compensation_ev",
        "print_exposure", "grain_active", "grain_amount", "halation_active", "halation_amount",
        "glare_active", "glare_amount", "y_filter_shift", "m_filter_shift", "film_format_mm"};
    for (const auto& [key, value] : j.fields()) {
        require(std::find(std::begin(allowed), std::end(allowed), key) != std::end(allowed), "Unknown control: " + key);
        require(!value.is_number() || std::isfinite(value.as_double()), "Non-finite control");
    }
    return j;
}
uint32_t argb(float r, float g, float b) {
    auto c = [](float x) { return uint32_t(std::lround(std::clamp(x, 0.f, 1.f) * 255)); };
    return 0xff000000u | c(r) << 16 | c(g) << 8 | c(b);
}
std::vector<uint32_t> copy_result(spk_result& r) {
    require(r.rgba16 && r.width && r.height && r.row_stride_px >= r.width, "Invalid native render");
    std::vector<uint32_t> out(size_t(r.width) * r.height);
    for (uint32_t y = 0; y < r.height; ++y) for (uint32_t x = 0; x < r.width; ++x) {
        const auto* p = r.rgba16 + (size_t(y) * r.row_stride_px + x) * 4;
        auto c = [](uint16_t v) { return (uint32_t(v) + 128) / 257; };
        out[size_t(y) * r.width + x] = 0xff000000u | c(p[0]) << 16 | c(p[1]) << 8 | c(p[2]);
    }
    return out;
}
struct Result { spk_result r{}; ~Result() { spk_result_free(&r); } };
void open(State& s, const std::vector<float>& rgb, int w, int h, bool raw,
          std::vector<uint32_t> original, const char* settings) {
    Json wanted = parameters(settings), config = wanted;
    config.set("input_color_space", Json(std::string(raw ? "ProPhoto RGB" : "sRGB")));
    config.set("input_cctf_decoding", Json(!raw));
    config.set("output_color_space", Json(std::string("sRGB")));
    config.set("output_cctf_encoding", Json(true));
    config.set("extended_dynamic_range", Json(false));
    config.set("auto_exposure", Json(false));
    const spk_image input{rgb.data(), uint32_t(w), uint32_t(h), 3};
    const auto json = config.dump();
    Session next(spk_open(s.engine.get(), &input, json.c_str(), nullptr), spk_session_release);
    require(bool(next), spk_last_error(s.engine.get()));
    Result result;
    const auto status = spk_render(next.get(), "full", &result.r);
    require(status == SPK_OK, spk_last_error(s.engine.get()));
    auto pixels = copy_result(result.r);
    // Publish only after a complete decode + render. Failed import retains the
    // previous source, settings and image, including the export pixels.
    s.session = std::move(next); s.settings = wanted;
    s.width = int(result.r.width); s.height = int(result.r.height);
    s.elapsed = result.r.elapsed_ms; s.pixels = std::move(pixels); s.original = std::move(original);
}
template<class F> int guarded(void* h, F fn) noexcept {
    if (!h) return 0;
    auto& s = *static_cast<State*>(h);
    try { fn(s); s.error.clear(); return 1; }
    catch (const std::exception& e) { s.error = e.what(); return 0; }
    catch (...) { s.error = "Native operation failed"; return 0; }
}
}
extern "C" {
void* sl_create(const char* resources) {
    try {
        require(resources != nullptr, "Missing resources");
        auto s = std::make_unique<State>();
        s->engine.reset(spk_engine_create(resources, nullptr));
        require(bool(s->engine), spk_last_error(nullptr));
        Json catalog = Json::object(), films = Json::array(), papers = Json::array();
        for (const auto& f : std::filesystem::directory_iterator(std::filesystem::u8path(resources) / "profiles")) {
            if (f.path().extension() != ".json") continue;
            const auto stem = f.path().stem().u8string();
            const std::string id(reinterpret_cast<const char*>(stem.data()), stem.size());
            spk::Profile profile; std::string error;
            // Use the engine's loader: derived stocks (CineStill 800T) inherit
            // their stage and type and do not repeat them in the JSON file.
            require(spk::load_profile(resources, id, profile, error), error);
            const auto& info = profile.info; Json item = Json::object();
            item.set("id", Json(id)); item.set("name", Json(info.name));
            item.set("positive", Json(info.is_positive()));
            item.set("monochrome", Json(info.channel_model == "bw"));
            if (info.stage == "filming") films.push(item);
            else if (info.stage == "printing") papers.push(item);
        }
        catalog.set("films", films); catalog.set("papers", papers); s->catalog = catalog.dump();
        create_error.clear(); return s.release();
    } catch (const std::exception& e) { create_error = e.what(); return nullptr; }
    catch (...) { create_error = "Engine initialization failed"; return nullptr; }
}
void sl_destroy(void* h) { delete static_cast<State*>(h); }
const char* sl_error(void* h) { return h ? static_cast<State*>(h)->error.c_str() : create_error.c_str(); }
const char* sl_catalog(void* h) { return h ? static_cast<State*>(h)->catalog.c_str() : "{}"; }
int sl_open_argb(void* h, const uint32_t* pixels, int w, int height, const char* settings) {
    return guarded(h, [&](State& s) {
        require(pixels && w > 0 && height > 0 && int64_t(w) * height <= 20000000, "Image dimensions exceed 20 MP working limit");
        size_t n = size_t(w) * height;
        std::vector<float> rgb(n * 3); std::vector<uint32_t> original(n);
        for (size_t i = 0; i < n; ++i) {
            float a = float(pixels[i] >> 24) / 255.f;
            float r = float((pixels[i] >> 16) & 255) / 255.f * a;
            float g = float((pixels[i] >> 8) & 255) / 255.f * a;
            float b = float(pixels[i] & 255) / 255.f * a;
            rgb[i*3] = r; rgb[i*3+1] = g; rgb[i*3+2] = b;
            original[i] = argb(r, g, b);
        }
        open(s, rgb, w, height, false, std::move(original), settings);
    });
}
int sl_open_raw(void* h, const char* path, int max_edge, const char* settings) {
    return guarded(h, [&](State& s) {
        require(path && max_edge >= 256 && max_edge <= 4096, "Invalid RAW size limit");
        spk::io::DecodedRaw raw; std::string error;
        require(spk::io::decode_raw_compatible(std::filesystem::u8path(path), raw, error), error);
        const double scale = std::min(1., double(max_edge) / std::max(raw.width, raw.height));
        int w = std::max(1, int(raw.width * scale)), hgt = std::max(1, int(raw.height * scale));
        std::vector<float> rgb(size_t(w) * hgt * 3);
        std::vector<uint32_t> original(size_t(w) * hgt);
        auto encode = [](float x) { return x <= .0031308f ? 12.92f*x : 1.055f*std::pow(x, 1/2.4f)-.055f; };
        for (int y = 0; y < hgt; ++y) for (int x = 0; x < w; ++x) {
            // Area average in scene-linear ProPhoto, before the film model.
            const unsigned x0 = uint64_t(x)*raw.width/w, x1 = uint64_t(x+1)*raw.width/w;
            const unsigned y0 = uint64_t(y)*raw.height/hgt, y1 = uint64_t(y+1)*raw.height/hgt;
            float v[3]{};
            for (unsigned yy=y0; yy<y1; ++yy) for (unsigned xx=x0; xx<x1; ++xx)
                for(int c=0;c<3;++c) v[c] += raw.rgb[(size_t(yy)*raw.width+xx)*3+c];
            for(int c=0;c<3;++c) rgb[(size_t(y)*w+x)*3+c] = v[c] /= float((x1-x0)*(y1-y0));
            // Bradford-adapted ProPhoto D50 -> linear sRGB D65, only for the
            // before view. Film input remains the original linear ProPhoto.
            original[size_t(y)*w+x] = argb(encode(2.03408f*v[0]-0.72733f*v[1]-0.30675f*v[2]),
                encode(-0.22881f*v[0]+1.23173f*v[1]-0.00292f*v[2]),
                encode(-0.00857f*v[0]-0.15329f*v[1]+1.16186f*v[2]));
        }
        raw.rgb.clear(); raw.rgb.shrink_to_fit();
        open(s, rgb, w, hgt, true, std::move(original), settings);
    });
}
int sl_render(void* h, const char* settings) {
    return guarded(h, [&](State& s) {
        require(bool(s.session), "Import an image first");
        Json wanted = parameters(settings), delta = Json::object();
        for (const auto& [key,value] : wanted.fields())
            if (value.dump() != s.settings.at(key).dump()) delta.set(key,value);
        const auto json = delta.dump();
        const auto applied = spk_set_params(s.session.get(),json.c_str(),nullptr);
        require(applied==SPK_OK,spk_last_error(s.engine.get()));
        try {
            Result result;
            const auto status = spk_reprint(s.session.get(),"full",&result.r);
            require(status==SPK_OK,spk_last_error(s.engine.get()));
            auto pixels = copy_result(result.r);
            s.pixels = std::move(pixels); s.settings = wanted; s.elapsed = result.r.elapsed_ms;
        } catch (...) {
            const auto previous = s.settings.dump();
            spk_set_params(s.session.get(),previous.c_str(),nullptr);
            throw;
        }
    });
}
const uint32_t* sl_pixels(void* h, int original) { if(!h) return nullptr; auto& s=*static_cast<State*>(h); return original?s.original.data():s.pixels.data(); }
int sl_width(void* h) { return h?static_cast<State*>(h)->width:0; }
int sl_height(void* h) { return h?static_cast<State*>(h)->height:0; }
double sl_elapsed(void* h) { return h?static_cast<State*>(h)->elapsed:0; }
}
