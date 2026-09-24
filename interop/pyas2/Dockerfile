FROM python:3.13-slim
RUN pip install --no-cache-dir pyas2lib==1.4.4 requests==2.34.2
WORKDIR /app
COPY server.py .
EXPOSE 8000
CMD ["python", "-u", "server.py"]
