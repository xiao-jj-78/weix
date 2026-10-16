/**
 * HTTP 客户端（org.json 为 Android 框架内置，零第三方依赖）。
 */
package com.wechatlike.remote

import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL

object Api {

    private const val TIMEOUT = 8000

    private fun request(
        ip: String,
        port: String,
        path: String,
        method: String = "GET",
        body: JSONObject? = null
    ): Pair<Int?, String?> {
        return try {
            val conn = URL("http://$ip:$port$path").openConnection() as HttpURLConnection
            conn.connectTimeout = TIMEOUT
            conn.readTimeout = TIMEOUT
            conn.requestMethod = method
            if (body != null) {
                conn.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                conn.doOutput = true
                OutputStreamWriter(conn.outputStream, "UTF-8").use { it.write(body.toString()) }
            }
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val text = stream?.let {
                BufferedReader(InputStreamReader(it, "UTF-8")).use { r -> r.readText() }
            }
            conn.disconnect()
            Pair(code, text)
        } catch (e: Exception) {
            Pair(null, e.javaClass.simpleName + ": " + (e.message ?: "网络错误"))
        }
    }

    fun ping(ip: String, port: String, cb: (Boolean, String) -> Unit) {
        Thread {
            val (code, text) = request(ip, port, "/api/ping")
            cb(code == 200, text ?: "无响应")
        }.start()
    }

    fun status(ip: String, port: String, cb: (JSONObject?) -> Unit) {
        Thread {
            val (code, text) = request(ip, port, "/api/status")
            val json = if (code == 200 && text != null) {
                try { JSONObject(text) } catch (e: Exception) { null }
            } else null
            cb(json)
        }.start()
    }

    fun task(ip: String, port: String, action: String, taskId: String, cb: (Boolean, String) -> Unit) {
        Thread {
            val (_, text) = request(
                ip, port, "/api/task/$action", "POST",
                JSONObject().put("task", taskId)
            )
            val msg = try {
                JSONObject(text ?: "").optString("message", text ?: "")
            } catch (e: Exception) { text ?: "" }
            cb(text?.contains("\"ok\":true") == true || text?.contains("\"ok\": true") == true, msg)
        }.start()
    }

    fun logs(ip: String, port: String, taskId: String, n: Int, cb: (List<String>) -> Unit) {
        Thread {
            val (code, text) = request(ip, port, "/api/logs?task=$taskId&n=$n")
            val lines = if (code == 200 && text != null) {
                try {
                    val arr = JSONObject(text).getJSONArray("lines")
                    (0 until arr.length()).map { arr.getString(it) }
                } catch (e: Exception) { emptyList() }
            } else emptyList()
            cb(lines)
        }.start()
    }
}
