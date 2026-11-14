"""Мини-замена scipy.signal.butter/sosfilt и scipy.io.wavfile только на numpy.

butter() возвращает описание фильтра Баттерворта; sosfilt() применяет его
точной частотной характеристикой билинейного преобразования (как у scipy)
через БПФ с нулевым добором — результат совпадает с каузальным IIR-фильтром
(хвост импульсной характеристики длиннее PAD отбрасывается).
"""
import wave
import numpy as np

PAD_SEC = 1.0


def butter(order, Wn, btype="low", output="sos"):
    return (order, Wn, btype)


def _response(desc, n_fft, fs=2.0):
    order, Wn, btype = desc
    w = np.linspace(0, np.pi, n_fft // 2 + 1)  # digital rad/sample
    fs2 = 2.0  # bilinear constant (fs = 1 sample-normalised, 2*fs = 2)
    S = 1j * fs2 * np.tan(np.clip(w, 0, np.pi * 0.999999) / 2)
    k = np.arange(order)
    poles = np.exp(1j * np.pi * (2 * k + order + 1) / (2 * order))  # analog prototype, wc = 1

    def proto(s):
        h = np.ones_like(s)
        for p in poles:
            h = h / (s - p)
        return h * np.real(np.prod(-poles))

    def warp(x):  # Wn in units of Nyquist
        return fs2 * np.tan(np.pi * x / 2)

    with np.errstate(divide="ignore", invalid="ignore"):
        if btype in ("low", "lowpass", "lp"):
            wc = warp(Wn)
            H = proto(S / wc)
        elif btype in ("high", "highpass", "hp"):
            wc = warp(Wn)
            H = proto(wc / np.where(S == 0, 1e-12j, S))
        else:  # band
            w1, w2 = warp(Wn[0]), warp(Wn[1])
            w0 = np.sqrt(w1 * w2)
            bw = w2 - w1
            s = np.where(S == 0, 1e-12j, S)
            H = proto((s * s + w0 * w0) / (s * bw))
    H = np.nan_to_num(H)
    return H


def _nfft(n):
    p = 1
    while p < n:
        p *= 2
    return p


def sosfilt(desc, x):
    x = np.asarray(x, dtype=float)
    n = x.shape[0]
    nf = _nfft(n + int(PAD_SEC * 44100))
    H = _response(desc, nf)
    X = np.fft.rfft(x, n=nf)
    return np.fft.irfft(X * H, n=nf)[:n]


class wavfile:
    @staticmethod
    def write(path, sr, data):
        data = np.asarray(data)
        ch = 1 if data.ndim == 1 else data.shape[1]
        with wave.open(path, "wb") as w:
            w.setnchannels(ch)
            w.setsampwidth(2)
            w.setframerate(sr)
            w.writeframes(data.astype("<i2").tobytes())

    @staticmethod
    def read(path):
        with wave.open(path, "rb") as w:
            sr = w.getframerate()
            ch = w.getnchannels()
            x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2")
        return sr, (x.reshape(-1, ch) if ch > 1 else x)
