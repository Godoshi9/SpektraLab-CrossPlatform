#include <jni.h>
#include "bridge.h"
#include <string>
namespace {
struct Utf { JNIEnv* env; jstring j; const char* value; Utf(JNIEnv* e,jstring s):env(e),j(s),value(e->GetStringUTFChars(s,nullptr)){} ~Utf(){if(value)env->ReleaseStringUTFChars(j,value);} };
void* handle(jlong p) { return reinterpret_cast<void*>(p); }
void fail(JNIEnv* env, void* p) { env->ThrowNew(env->FindClass("java/lang/IllegalStateException"),sl_error(p)); }
}
extern "C" {
JNIEXPORT jlong JNICALL Java_app_spektralab_mobile_NativeEngine_create(JNIEnv* e,jclass,jstring path) { Utf p(e,path); auto h=sl_create(p.value); if(!h)fail(e,h); return reinterpret_cast<jlong>(h); }
JNIEXPORT void JNICALL Java_app_spektralab_mobile_NativeEngine_destroy(JNIEnv*,jclass,jlong h) {sl_destroy(handle(h));}
JNIEXPORT jstring JNICALL Java_app_spektralab_mobile_NativeEngine_catalog(JNIEnv* e,jclass,jlong h) {return e->NewStringUTF(sl_catalog(handle(h)));}
JNIEXPORT void JNICALL Java_app_spektralab_mobile_NativeEngine_openPixels(JNIEnv* e,jclass,jlong h,jintArray data,jint w,jint height,jstring settings) {
    if(w<=0||height<=0||int64_t(w)*height>e->GetArrayLength(data)) { e->ThrowNew(e->FindClass("java/lang/IllegalArgumentException"),"Invalid pixel array"); return; }
    Utf s(e,settings); jint* p=e->GetIntArrayElements(data,nullptr);
    if(!p)return;
    int ok=sl_open_argb(handle(h),reinterpret_cast<uint32_t*>(p),w,height,s.value);
    e->ReleaseIntArrayElements(data,p,JNI_ABORT); if(!ok)fail(e,handle(h));
}
JNIEXPORT void JNICALL Java_app_spektralab_mobile_NativeEngine_openRaw(JNIEnv* e,jclass,jlong h,jstring path,jint edge,jstring settings) {Utf p(e,path),s(e,settings);if(!sl_open_raw(handle(h),p.value,edge,s.value))fail(e,handle(h));}
JNIEXPORT void JNICALL Java_app_spektralab_mobile_NativeEngine_render(JNIEnv* e,jclass,jlong h,jstring settings) {Utf s(e,settings);if(!sl_render(handle(h),s.value))fail(e,handle(h));}
JNIEXPORT jintArray JNICALL Java_app_spektralab_mobile_NativeEngine_pixels(JNIEnv* e,jclass,jlong h,jboolean original) {int n=sl_width(handle(h))*sl_height(handle(h));auto a=e->NewIntArray(n);if(a&&n)e->SetIntArrayRegion(a,0,n,reinterpret_cast<const jint*>(sl_pixels(handle(h),original)));return a;}
JNIEXPORT jint JNICALL Java_app_spektralab_mobile_NativeEngine_width(JNIEnv*,jclass,jlong h){return sl_width(handle(h));}
JNIEXPORT jint JNICALL Java_app_spektralab_mobile_NativeEngine_height(JNIEnv*,jclass,jlong h){return sl_height(handle(h));}
JNIEXPORT jdouble JNICALL Java_app_spektralab_mobile_NativeEngine_elapsed(JNIEnv*,jclass,jlong h){return sl_elapsed(handle(h));}
}
