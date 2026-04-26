import base64
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import padding
from cryptography.hazmat.backends import default_backend

key_b64 = "7xLQ/syxeVOQNNBfHrVj7Slw71OepMdPT13AVj5GDp4="
iv_b64 = "wsx0yZ9zSARVA6PrifH2tg=="
cipher_text_b64 = "lTuiRnPrGsjtmjba/XNGWueBKzQ87u27R6x7dIEB2lKJqlrTBVTKlBZCnMD4rBdwNFzYe/35i1d4fV2XCrwK50iqH58VydO+5MQoQmf09Pc="

key = base64.b64decode(key_b64)
iv = base64.b64decode(iv_b64)
cipher_text = base64.b64decode(cipher_text_b64)

try:
    cipher = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend())
    decryptor = cipher.decryptor()
    padded_plain_text = decryptor.update(cipher_text) + decryptor.finalize()
    
    unpadder = padding.PKCS7(128).unpadder()
    plain_text = unpadder.update(padded_plain_text) + unpadder.finalize()
    
    print(f"Decrypted: {plain_text.decode('utf-8')}")
except Exception as e:
    print(f"Decryption failed: {e}")
